class ChatApp {
    constructor() {
        this.microphoneSelect = document.getElementById('microphone-select');
        this.toggleButton = document.getElementById('toggle-listening');
        this.statusElement = document.getElementById('status');
        this.statusText = this.statusElement.querySelector('.status-text');
        this.messagesContainer = document.getElementById('messages');
        this.messageCountElement = document.getElementById('message-count');
        this.durationElement = document.getElementById('duration');
        this.textInput = document.getElementById('text-input');
        this.sendTextButton = document.getElementById('send-text-button');
        this.toggleAudioButton = document.getElementById('toggle-audio');
        this.audioIcon = document.getElementById('audio-icon');
        this.audioStatusText = document.getElementById('audio-status-text');
        this.remoteAudio = document.getElementById('remote-audio');

        this.peerConnection = null;
        this.dataChannel = null;
        this.mediaStream = null;
        this.sessionId = null;
        this.sessionStarted = false;
        this.isListening = false;
        this.audioEnabled = true;
        this.messageCount = 0;
        this.startTime = null;
        this.durationInterval = null;
        this.transcriptGroups = new Map();
        this.init();
    }

    async init() {
        this.setupEventListeners();
        await this.loadMicrophones();
    }

    setupEventListeners() {
        this.microphoneSelect.addEventListener('change', event => {
            this.toggleButton.disabled = !event.target.value;
            if (event.target.value) this.updateStatus('Prêt', 'ready');
        });
        this.toggleButton.addEventListener('click', () => this.toggleListening());
        this.sendTextButton.addEventListener('click', () => this.sendTextMessage());
        this.textInput.addEventListener('keydown', event => {
            if (event.key === 'Enter' && !event.shiftKey) {
                event.preventDefault();
                this.sendTextMessage();
            }
        });
        this.toggleAudioButton.addEventListener('click', () => this.toggleAudioPlayback());
        window.addEventListener('beforeunload', () => this.releaseSession(false));
    }

    async loadMicrophones() {
        try {
            const permissionStream = await navigator.mediaDevices.getUserMedia({ audio: true });
            permissionStream.getTracks().forEach(track => track.stop());
            const devices = (await navigator.mediaDevices.enumerateDevices())
                .filter(device => device.kind === 'audioinput');
            this.microphoneSelect.innerHTML = '';
            devices.forEach((device, index) => {
                const option = document.createElement('option');
                option.value = device.deviceId;
                option.textContent = device.label || `Microphone ${index + 1}`;
                this.microphoneSelect.appendChild(option);
            });
            this.toggleButton.disabled = devices.length === 0;
            this.updateStatus(devices.length ? 'Prêt' : 'Aucun microphone détecté', devices.length ? 'ready' : '');
        } catch (error) {
            console.error(error);
            this.updateStatus('Accès au microphone refusé');
            this.addSystemMessage('❌ Autorisez le microphone pour démarrer une session Live.');
        }
    }

    async toggleListening() {
        if (this.isListening) await this.stopListening();
        else await this.startListening();
    }

    async startListening() {
        try {
            this.removeWelcomeMessage();
            this.updateStatus('Connexion Live…', 'listening');
            await this.connectLiveSession();
            this.isListening = true;
            this.startTime = Date.now();
            this.startDurationCounter();
            this.toggleButton.classList.add('listening');
            this.toggleButton.querySelector('.btn-text').textContent = "Arrêter l'écoute";
            this.toggleButton.querySelector('.btn-icon').textContent = '⏹️';
            this.updateStatus('En écoute…', 'listening');
        } catch (error) {
            console.error(error);
            this.addSystemMessage(`❌ Impossible de démarrer la session Live : ${error.message}`);
            await this.releaseSession(true);
            this.updateStatus('Erreur');
        }
    }

    async connectLiveSession() {
        if (this.peerConnection && this.sessionStarted) return;
        this.mediaStream = await navigator.mediaDevices.getUserMedia({
            audio: {
                deviceId: this.microphoneSelect.value ? { exact: this.microphoneSelect.value } : undefined,
                echoCancellation: true,
                noiseSuppression: true,
                autoGainControl: true
            }
        });

        const peer = new RTCPeerConnection();
        this.peerConnection = peer;
        this.mediaStream.getTracks().forEach(track => peer.addTrack(track, this.mediaStream));
        peer.ontrack = event => {
            this.remoteAudio.srcObject = event.streams[0];
            this.remoteAudio.play().catch(error => console.debug('Lecture audio différée', error));
        };
        peer.onconnectionstatechange = () => {
            if (['failed', 'disconnected'].includes(peer.connectionState)) this.updateStatus('Connexion Live interrompue');
        };

        const channel = peer.createDataChannel('oai-events');
        this.dataChannel = channel;
        channel.onmessage = event => this.handleLiveEvent(JSON.parse(event.data));
        channel.onerror = error => console.error('Canal Live', error);
        channel.onclose = () => {
            this.sessionStarted = false;
            if (this.isListening) this.updateStatus('Session Live fermée');
        };

        const offer = await peer.createOffer();
        await peer.setLocalDescription(offer);
        await this.waitForIceGathering(peer);
        const response = await fetch('/api/live/session', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ sdp: peer.localDescription.sdp })
        });
        if (!response.ok) {
            const problem = await response.json().catch(() => ({}));
            throw new Error(problem.detail || `HTTP ${response.status}`);
        }
        const live = await response.json();
        this.sessionId = live.sessionId;
        await peer.setRemoteDescription({ type: 'answer', sdp: live.sdp });
        await this.waitForSessionStarted();
    }

    waitForIceGathering(peer) {
        if (peer.iceGatheringState === 'complete') return Promise.resolve();
        return new Promise((resolve, reject) => {
            const timeout = setTimeout(() => reject(new Error('Délai ICE dépassé')), 10000);
            const listener = () => {
                if (peer.iceGatheringState === 'complete') {
                    clearTimeout(timeout);
                    peer.removeEventListener('icegatheringstatechange', listener);
                    resolve();
                }
            };
            peer.addEventListener('icegatheringstatechange', listener);
        });
    }

    waitForSessionStarted() {
        if (this.sessionStarted) return Promise.resolve();
        return new Promise((resolve, reject) => {
            const timeout = setTimeout(() => {
                document.removeEventListener('live-session-started', listener);
                reject(new Error("La session Live n'a pas démarré"));
            }, 15000);
            const listener = () => {
                clearTimeout(timeout);
                resolve();
            };
            document.addEventListener('live-session-started', listener, { once: true });
        });
    }

    handleLiveEvent(message) {
        switch (message.type) {
            case 'session.started':
                this.sessionStarted = true;
                document.dispatchEvent(new Event('live-session-started'));
                this.updateStatus('Connecté à GPT-Live', 'ready');
                break;
            case 'session.input_transcript.delta':
                this.appendTranscript('user', message);
                break;
            case 'session.output_transcript.delta':
                this.appendTranscript('assistant', message);
                break;
            case 'error':
                this.addSystemMessage(`❌ ${message.error?.message || 'Erreur Live'}`);
                this.updateStatus('Erreur');
                break;
            case 'session.closed':
                this.addSystemMessage(`Session terminée (${message.reason || 'fermée'}).`);
                this.releaseSession(true);
                break;
            default:
                console.debug('Événement Live ignoré', message.type);
        }
    }

    appendTranscript(role, event) {
        const start = Number(event.start_ms || 0);
        const end = Number(event.end_ms || start);
        const eventId = event.event_id || `${role}-${start}`;
        const previous = this.transcriptGroups.get(role);
        const sameGroup = previous && (previous.eventIds.has(eventId) || start <= previous.end + 250);
        if (sameGroup) {
            previous.eventIds.add(eventId);
            previous.end = Math.max(previous.end, end);
            previous.text += event.delta || '';
            previous.element.querySelector('.message-text').textContent = previous.text;
        } else {
            const element = role === 'user'
                ? this.createMessageElement('user', '👤', 'Vous', event.delta || '')
                : this.createMessageElement('ai', '🤖', 'IA', event.delta || '');
            element.dataset.liveStart = String(start);
            element.dataset.liveEnd = String(end);
            this.messagesContainer.appendChild(element);
            this.transcriptGroups.set(role, { element, text: event.delta || '', end, eventIds: new Set([eventId]) });
            this.updateMessageCount();
        }
        this.scrollToBottom();
    }

    async sendTextMessage() {
        const text = this.textInput.value.trim();
        if (!text) return;
        try {
            this.removeWelcomeMessage();
            if (!this.sessionStarted) await this.startListening();
            if (!this.dataChannel || this.dataChannel.readyState !== 'open') throw new Error('Le canal Live n’est pas prêt');
            this.addUserMessage(text);
            this.textInput.value = '';
            this.dataChannel.send(JSON.stringify({
                type: 'response.item.create',
                event_id: `text_${crypto.randomUUID()}`,
                item: { type: 'message', role: 'user', content: [{ type: 'input_text', text }] }
            }));
            this.dataChannel.send(JSON.stringify({ type: 'response.create', event_id: `response_${crypto.randomUUID()}` }));
        } catch (error) {
            console.error(error);
            this.addSystemMessage(`❌ Envoi impossible : ${error.message}`);
        }
    }

    async stopListening() {
        await this.releaseSession(true);
        this.addSystemMessage('⏸️ Écoute arrêtée.');
    }

    async releaseSession(updateUi) {
        this.isListening = false;
        this.sessionStarted = false;
        this.stopDurationCounter();
        if (this.dataChannel?.readyState === 'open') {
            this.dataChannel.send(JSON.stringify({ type: 'session.close', event_id: `close_${crypto.randomUUID()}` }));
        }
        this.dataChannel?.close();
        this.dataChannel = null;
        this.peerConnection?.close();
        this.peerConnection = null;
        this.mediaStream?.getTracks().forEach(track => track.stop());
        this.mediaStream = null;
        this.remoteAudio.srcObject = null;
        const sessionId = this.sessionId;
        this.sessionId = null;
        if (sessionId) {
            fetch(`/api/live/session/${encodeURIComponent(sessionId)}`, { method: 'DELETE', keepalive: true }).catch(() => {});
        }
        if (updateUi) {
            this.toggleButton.classList.remove('listening');
            this.toggleButton.querySelector('.btn-text').textContent = "Démarrer l'écoute";
            this.toggleButton.querySelector('.btn-icon').textContent = '🎤';
            this.updateStatus('Prêt', 'ready');
        }
    }

    toggleAudioPlayback() {
        this.audioEnabled = !this.audioEnabled;
        this.remoteAudio.muted = !this.audioEnabled;
        this.audioIcon.textContent = this.audioEnabled ? '🔊' : '🔇';
        this.audioStatusText.textContent = this.audioEnabled ? 'Audio activé' : 'Audio désactivé';
        this.toggleAudioButton.classList.toggle('bg-green-600', this.audioEnabled);
        this.toggleAudioButton.classList.toggle('hover:bg-green-700', this.audioEnabled);
        this.toggleAudioButton.classList.toggle('bg-gray-600', !this.audioEnabled);
        this.toggleAudioButton.classList.toggle('hover:bg-gray-700', !this.audioEnabled);
    }

    startDurationCounter() {
        this.durationInterval = setInterval(() => {
            const elapsed = Math.floor((Date.now() - this.startTime) / 1000);
            const minutes = Math.floor(elapsed / 60).toString().padStart(2, '0');
            const seconds = (elapsed % 60).toString().padStart(2, '0');
            this.durationElement.textContent = `${minutes}:${seconds}`;
        }, 1000);
    }

    stopDurationCounter() {
        clearInterval(this.durationInterval);
        this.durationInterval = null;
    }

    updateStatus(text, status = '') {
        this.statusText.textContent = text;
        this.statusElement.className = `status ${status}`;
    }

    removeWelcomeMessage() { this.messagesContainer.querySelector('.welcome-message')?.remove(); }

    addUserMessage(text) {
        this.messagesContainer.appendChild(this.createMessageElement('user', '👤', 'Vous', text));
        this.updateMessageCount();
        this.scrollToBottom();
    }

    addSystemMessage(text) {
        const element = document.createElement('div');
        element.className = 'message system text-center py-2';
        const content = document.createElement('div');
        content.className = 'inline-block px-4 py-2 bg-blue-50 text-blue-700 text-sm rounded-md border border-blue-200';
        content.textContent = text;
        element.appendChild(content);
        this.messagesContainer.appendChild(element);
        this.scrollToBottom();
    }

    createMessageElement(type, avatar, sender, text) {
        const element = document.createElement('div');
        const isUser = type === 'user';
        element.className = `message ${type} flex gap-3 ${isUser ? 'flex-row-reverse' : ''}`;
        element.innerHTML = `
            <div class="flex-shrink-0 w-8 h-8 rounded-full flex items-center justify-center text-lg ${isUser ? 'bg-blue-100' : 'bg-gray-200'}">${avatar}</div>
            <div class="max-w-[70%]">
                <div class="flex items-center gap-2 mb-1 ${isUser ? 'flex-row-reverse' : ''}">
                    <span class="text-xs font-semibold text-gray-700">${sender}</span>
                    <span class="text-xs ${isUser ? 'text-blue-200' : 'text-gray-500'}">${new Date().toLocaleTimeString('fr-FR', { hour: '2-digit', minute: '2-digit' })}</span>
                </div>
                <div class="${isUser ? 'bg-blue-600 text-white' : 'bg-gray-100 text-gray-900'} px-4 py-2 rounded-lg text-sm leading-relaxed message-text"></div>
            </div>`;
        element.querySelector('.message-text').textContent = text;
        return element;
    }

    updateMessageCount() {
        this.messageCount++;
        this.messageCountElement.textContent = `${this.messageCount} message${this.messageCount > 1 ? 's' : ''}`;
    }

    scrollToBottom() { this.messagesContainer.scrollTop = this.messagesContainer.scrollHeight; }
}

document.addEventListener('DOMContentLoaded', () => new ChatApp());
