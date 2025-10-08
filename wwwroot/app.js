class ChatApp {
    constructor() {
        // DOM Elements
        this.microphoneSelect = document.getElementById('microphone-select');
        this.toggleButton = document.getElementById('toggle-listening');
        this.statusElement = document.getElementById('status');
        this.statusText = this.statusElement.querySelector('.status-text');
        this.messagesContainer = document.getElementById('messages');
        this.messageCountElement = document.getElementById('message-count');
        this.durationElement = document.getElementById('duration');

        // State
        this.isListening = false;
        this.ws = null;
        this.messageCount = 0;
        this.startTime = null;
        this.durationInterval = null;
        this.selectedMicrophone = null;
        this.audioContext = null;
        this.mediaStream = null;
        this.audioWorkletNode = null;
        this.currentTranscript = { user: '', assistant: '' };
        this.audioQueue = [];
        this.isPlayingAudio = false;
        this.currentResponseId = null;

        // Initialize
        this.init();
    }

    async init() {
        await this.loadMicrophones();
        this.setupEventListeners();
    }

    async loadMicrophones() {
        try {
            // Request microphone permission
            await navigator.mediaDevices.getUserMedia({ audio: true });

            // Get all audio input devices
            const devices = await navigator.mediaDevices.enumerateDevices();
            const microphones = devices.filter(device => device.kind === 'audioinput');

            // Clear and populate microphone select
            this.microphoneSelect.innerHTML = '<option value="">Sélectionnez un microphone...</option>';
            
            microphones.forEach((mic, index) => {
                const option = document.createElement('option');
                option.value = mic.deviceId;
                option.textContent = mic.label || `Microphone ${index + 1}`;
                this.microphoneSelect.appendChild(option);
            });

            if (microphones.length > 0) {
                this.updateStatus('Prêt - Sélectionnez un microphone');
            } else {
                this.updateStatus('Aucun microphone détecté');
            }
        } catch (error) {
            console.error('Erreur lors du chargement des microphones:', error);
            this.updateStatus('Erreur: Accès au microphone refusé');
            this.addSystemMessage('❌ Impossible d\'accéder au microphone. Veuillez autoriser l\'accès dans les paramètres de votre navigateur.');
        }
    }

    setupEventListeners() {
        this.microphoneSelect.addEventListener('change', (e) => {
            this.selectedMicrophone = e.target.value;
            this.toggleButton.disabled = !this.selectedMicrophone;
            if (this.selectedMicrophone) {
                this.updateStatus('Prêt', 'ready');
            }
        });

        this.toggleButton.addEventListener('click', () => {
            this.toggleListening();
        });
    }

    async connectWebSocket() {
        return new Promise((resolve, reject) => {
            const protocol = window.location.protocol === 'https:' ? 'wss:' : 'ws:';
            const wsUrl = `${protocol}//${window.location.host}/ws/realtime`;

            this.ws = new WebSocket(wsUrl);

            this.ws.onopen = () => {
                console.log('WebSocket connecté');
                this.updateStatus('Connexion au serveur...');
            };

            this.ws.onmessage = async (event) => {
                try {
                    const message = JSON.parse(event.data);
                    await this.handleServerMessage(message);
                } catch (error) {
                    console.error('Erreur lors du traitement du message:', error);
                }
            };

            this.ws.onerror = (error) => {
                console.error('Erreur WebSocket:', error);
                this.addSystemMessage('❌ Erreur de connexion au serveur');
                reject(error);
            };

            this.ws.onclose = () => {
                console.log('WebSocket déconnecté');
                if (this.isListening) {
                    this.stopListening();
                }
                this.ws = null;
            };

            // Wait for ready message
            const readyHandler = (event) => {
                const message = JSON.parse(event.data);
                if (message.type === 'ready') {
                    this.ws.removeEventListener('message', readyHandler);
                    resolve();
                }
            };
            this.ws.addEventListener('message', readyHandler);

            // Timeout after 10 seconds
            setTimeout(() => reject(new Error('Connection timeout')), 10000);
        });
    }

    async handleServerMessage(message) {
        console.log('[Client] Message du serveur:', message.type, message);

        switch (message.type) {
            case 'ready':
                this.updateStatus('Connecté - Prêt à écouter', 'ready');
                this.addSystemMessage('✅ Connecté à OpenAI. Vous pouvez commencer à parler.');
                break;

            case 'status':
                this.updateStatus(message.status, message.status.includes('Ready') ? 'ready' : 'listening');
                break;

            case 'audio':
                // Queue audio for playback
                if (message.audio) {
                    console.log('[Client] Audio reçu, taille:', message.audio.length);
                    this.audioQueue.push(message.audio);
                    console.log('[Client] isPlayingAudio:', this.isPlayingAudio, 'Queue length:', this.audioQueue.length);
                    if (!this.isPlayingAudio) {
                        console.log('[Client] Démarrage lecture audio');
                        this.playAudioQueue();
                    } else {
                        console.log('[Client] Audio ajouté à la queue (lecture déjà en cours)');
                    }
                }
                break;

            case 'transcript':
                this.handleTranscript(message.role, message.transcript);
                break;

            case 'error':
                this.addSystemMessage(`❌ Erreur: ${message.error}`);
                this.updateStatus('Erreur');
                break;

            default:
                console.log('Type de message inconnu:', message.type);
        }
    }

    handleTranscript(role, transcript) {
        console.log('[Client] Transcript reçu - Role:', role, 'Texte:', transcript);
        
        if (role === 'user') {
            // User transcript arrives complete from the server
            // Note: It may arrive AFTER the assistant response has started
            console.log('[Client] Traitement transcript utilisateur:', transcript);
            if (transcript && transcript.trim()) {
                console.log('[Client] Ajout message utilisateur à l\'interface');
                this.addUserMessageBeforeLastAssistant(transcript);
            } else {
                console.warn('[Client] Transcript utilisateur vide ou invalide');
            }
        } else if (role === 'assistant') {
            // Assistant transcript arrives as deltas
            this.updateOrAddAssistantMessage(transcript);
        } else if (role === 'system' && transcript === '__RESPONSE_DONE__') {
            // Mark current response as complete
            this.finalizeCurrentAssistantMessage();
        }
    }

    updateOrAddAssistantMessage(delta) {
        // Find the last assistant message that is still streaming
        const messages = this.messagesContainer.querySelectorAll('.message.ai');
        let lastStreamingMessage = null;
        
        // Find the last message that is still streaming
        for (let i = messages.length - 1; i >= 0; i--) {
            if (messages[i].dataset.streaming === 'true') {
                lastStreamingMessage = messages[i];
                break;
            }
        }
        
        if (lastStreamingMessage) {
            // Update existing streaming message
            const textElement = lastStreamingMessage.querySelector('.message-text');
            textElement.textContent += delta;
            this.scrollToBottom();
        } else {
            // Create new assistant message
            const message = this.createMessageElement('ai', '🤖', 'IA', delta);
            message.dataset.streaming = 'true';
            message.dataset.responseId = Date.now(); // Unique ID for this response
            this.messagesContainer.appendChild(message);
            this.scrollToBottom();
            this.updateMessageCount();
        }
    }

    finalizeCurrentAssistantMessage() {
        // Mark the current streaming message as complete
        const messages = this.messagesContainer.querySelectorAll('.message.ai');
        for (let i = messages.length - 1; i >= 0; i--) {
            if (messages[i].dataset.streaming === 'true') {
                messages[i].dataset.streaming = 'false';
                console.log('Message assistant finalisé');
                break;
            }
        }
    }

    async playAudioQueue() {
        if (this.audioQueue.length === 0) {
            this.isPlayingAudio = false;
            console.log('[Client] Queue audio vide');
            return;
        }

        this.isPlayingAudio = true;
        const base64Audio = this.audioQueue.shift();
        console.log('[Client] Lecture audio, reste dans la queue:', this.audioQueue.length);

        try {
            // Decode base64 to raw PCM16 data
            const binaryString = atob(base64Audio);
            const bytes = new Uint8Array(binaryString.length);
            for (let i = 0; i < binaryString.length; i++) {
                bytes[i] = binaryString.charCodeAt(i);
            }

            // Convert to Int16Array (PCM16)
            const pcm16 = new Int16Array(bytes.buffer);

            // Create audio context for playback if not exists
            if (!this.playbackContext) {
                this.playbackContext = new (window.AudioContext || window.webkitAudioContext)({
                    sampleRate: 24000
                });
            }

            // Convert PCM16 to Float32 for Web Audio API
            const float32 = new Float32Array(pcm16.length);
            for (let i = 0; i < pcm16.length; i++) {
                float32[i] = pcm16[i] / 32768.0;
            }

            // Create audio buffer
            const audioBuffer = this.playbackContext.createBuffer(1, float32.length, 24000);
            audioBuffer.getChannelData(0).set(float32);

            // Play the audio
            const source = this.playbackContext.createBufferSource();
            source.buffer = audioBuffer;
            source.connect(this.playbackContext.destination);
            
            source.onended = () => {
                this.playAudioQueue(); // Play next in queue
            };

            source.start();
        } catch (error) {
            console.error('Erreur lors de la lecture audio:', error);
            this.playAudioQueue(); // Continue with next audio
        }
    }

    async toggleListening() {
        if (this.isListening) {
            await this.stopListening();
        } else {
            await this.startListening();
        }
    }

    async startListening() {
        try {
            // Clear welcome message if present
            const welcomeMessage = this.messagesContainer.querySelector('.welcome-message');
            if (welcomeMessage) {
                welcomeMessage.remove();
            }

            // Connect to WebSocket server
            this.updateStatus('Connexion...');
            await this.connectWebSocket();

            // Set up audio capture
            await this.setupAudioCapture();

            this.isListening = true;
            this.startTime = Date.now();
            this.startDurationCounter();

            // Update UI
            this.toggleButton.classList.add('listening');
            this.toggleButton.querySelector('.btn-text').textContent = 'Arrêter l\'écoute';
            this.toggleButton.querySelector('.btn-icon').textContent = '⏹️';
            this.updateStatus('En écoute...', 'listening');

        } catch (error) {
            console.error('Erreur lors du démarrage:', error);
            this.addSystemMessage('❌ Impossible de démarrer l\'écoute: ' + error.message);
            await this.stopListening();
        }
    }

    async setupAudioCapture() {
        try {
            // Get microphone stream
            this.mediaStream = await navigator.mediaDevices.getUserMedia({
                audio: {
                    deviceId: this.selectedMicrophone ? { exact: this.selectedMicrophone } : undefined,
                    sampleRate: 24000,
                    channelCount: 1,
                    echoCancellation: true,
                    noiseSuppression: true,
                    autoGainControl: true
                }
            });

            // Create audio context
            this.audioContext = new (window.AudioContext || window.webkitAudioContext)({
                sampleRate: 24000
            });

            const source = this.audioContext.createMediaStreamSource(this.mediaStream);

            // Create script processor for audio processing
            const bufferSize = 4096;
            const processor = this.audioContext.createScriptProcessor(bufferSize, 1, 1);

            processor.onaudioprocess = (e) => {
                if (!this.isListening || !this.ws || this.ws.readyState !== WebSocket.OPEN) {
                    return;
                }

                const inputData = e.inputBuffer.getChannelData(0);
                
                // Convert Float32 to Int16 (PCM16)
                const pcm16 = new Int16Array(inputData.length);
                for (let i = 0; i < inputData.length; i++) {
                    const s = Math.max(-1, Math.min(1, inputData[i]));
                    pcm16[i] = s < 0 ? s * 0x8000 : s * 0x7FFF;
                }

                // Convert to base64
                const base64 = this.arrayBufferToBase64(pcm16.buffer);

                // Send to server
                this.ws.send(JSON.stringify({
                    type: 'audio',
                    audio: base64
                }));
            };

            source.connect(processor);
            processor.connect(this.audioContext.destination);

            this.audioWorkletNode = processor;

        } catch (error) {
            console.error('Erreur lors de la configuration audio:', error);
            throw error;
        }
    }

    arrayBufferToBase64(buffer) {
        let binary = '';
        const bytes = new Uint8Array(buffer);
        const len = bytes.byteLength;
        for (let i = 0; i < len; i++) {
            binary += String.fromCharCode(bytes[i]);
        }
        return btoa(binary);
    }

    async stopListening() {
        this.isListening = false;
        this.stopDurationCounter();

        // Stop audio capture
        if (this.audioWorkletNode) {
            this.audioWorkletNode.disconnect();
            this.audioWorkletNode = null;
        }

        if (this.audioContext) {
            await this.audioContext.close();
            this.audioContext = null;
        }

        if (this.mediaStream) {
            this.mediaStream.getTracks().forEach(track => track.stop());
            this.mediaStream = null;
        }

        // Close WebSocket
        if (this.ws && this.ws.readyState === WebSocket.OPEN) {
            this.ws.close();
        }

        // Update UI
        this.toggleButton.classList.remove('listening');
        this.toggleButton.querySelector('.btn-text').textContent = 'Démarrer l\'écoute';
        this.toggleButton.querySelector('.btn-icon').textContent = '🎤';
        this.updateStatus('Prêt', 'ready');

        this.addSystemMessage('⏸️ Écoute arrêtée.');
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
        if (this.durationInterval) {
            clearInterval(this.durationInterval);
            this.durationInterval = null;
        }
    }

    updateStatus(text, status = '') {
        this.statusText.textContent = text;
        this.statusElement.className = `status ${status}`;
    }

    addUserMessage(text) {
        const message = this.createMessageElement('user', '👤', 'Vous', text);
        this.messagesContainer.appendChild(message);
        this.scrollToBottom();
        this.updateMessageCount();
    }

    addUserMessageBeforeLastAssistant(text) {
        // Find the last assistant message
        const assistantMessages = this.messagesContainer.querySelectorAll('.message.ai');
        
        const message = this.createMessageElement('user', '👤', 'Vous', text);
        
        if (assistantMessages.length > 0) {
            // Insert before the last assistant message
            const lastAssistant = assistantMessages[assistantMessages.length - 1];
            this.messagesContainer.insertBefore(message, lastAssistant);
            console.log('[Client] Message utilisateur inséré avant la réponse de l\'assistant');
        } else {
            // No assistant message yet, just append
            this.messagesContainer.appendChild(message);
        }
        
        this.scrollToBottom();
        this.updateMessageCount();
    }

    addSystemMessage(text) {
        const messageDiv = document.createElement('div');
        messageDiv.className = 'message system';
        messageDiv.innerHTML = `
            <div class="message-content" style="max-width: 100%; text-align: center; background: #f0f9ff; color: #0369a1; font-size: 14px;">
                ${text}
            </div>
        `;
        this.messagesContainer.appendChild(messageDiv);
        this.scrollToBottom();
    }

    createMessageElement(type, avatar, sender, text) {
        const messageDiv = document.createElement('div');
        messageDiv.className = `message ${type}`;
        
        const time = new Date().toLocaleTimeString('fr-FR', { 
            hour: '2-digit', 
            minute: '2-digit' 
        });

        messageDiv.innerHTML = `
            <div class="message-avatar">${avatar}</div>
            <div class="message-content">
                <div class="message-header">
                    <span class="message-sender">${sender}</span>
                    <span class="message-time">${time}</span>
                </div>
                <div class="message-text">${this.escapeHtml(text)}</div>
            </div>
        `;

        return messageDiv;
    }

    updateMessageCount() {
        this.messageCount++;
        const plural = this.messageCount > 1 ? 's' : '';
        this.messageCountElement.textContent = `${this.messageCount} message${plural}`;
    }

    scrollToBottom() {
        this.messagesContainer.scrollTop = this.messagesContainer.scrollHeight;
    }

    escapeHtml(text) {
        const div = document.createElement('div');
        div.textContent = text;
        return div.innerHTML;
    }
}

// Initialize the app when DOM is ready
document.addEventListener('DOMContentLoaded', () => {
    new ChatApp();
});
