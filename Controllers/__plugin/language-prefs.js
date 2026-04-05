(function() {
    const ALERT_LOADING = 'loading';
    const ALERT_SUCCESS = 'success';
    const ALERT_ERROR = 'error';

    const LANGUAGE_NAMES = {
        'eng': 'English',
        'deu': 'German',
        'spa': 'Spanish',
        'fra': 'French',
        'ita': 'Italian',
        'por': 'Portuguese',
        'rus': 'Russian',
        'jpn': 'Japanese',
        'kor': 'Korean',
        'zho': 'Chinese',
        'nld': 'Dutch',
        'pol': 'Polish',
        'swe': 'Swedish',
        'fin': 'Finnish',
        'nor': 'Norwegian',
        'dan': 'Danish',
        'ces': 'Czech',
        'hun': 'Hungarian',
        'ron': 'Romanian',
        'ukr': 'Ukrainian'
    };

    const elements = {
        alertBox: null,
        alertText: null,
        audioList: null,
        subtitleList: null,
        audioSelect: null,
        subtitleSelect: null,
        addAudioBtn: null,
        addSubtitleBtn: null,
        saveBtn: null,
        form: null
    };

    let state = {
        audioLanguages: [],
        subtitleLanguages: [],
        draggedItem: null
    };

    function init() {
        elements.alertBox = document.getElementById('alertBox');
        elements.alertText = document.getElementById('alertText');
        elements.audioList = document.getElementById('audioLanguages');
        elements.subtitleList = document.getElementById('subtitleLanguages');
        elements.audioSelect = document.getElementById('audioLangSelect');
        elements.subtitleSelect = document.getElementById('subtitleLangSelect');
        elements.addAudioBtn = document.getElementById('addAudioLang');
        elements.addSubtitleBtn = document.getElementById('addSubtitleLang');
        elements.saveBtn = document.getElementById('savePrefs');
        elements.form = document.getElementById('langPrefsForm');

        elements.addAudioBtn.addEventListener('click', () => addLanguage('audio'));
        elements.addSubtitleBtn.addEventListener('click', () => addLanguage('subtitle'));
        elements.form.addEventListener('submit', savePreferences);

        loadPreferences();
    }

    function showAlert(type, message) {
        elements.alertBox.className = `terminal-alert ${type}`;
        elements.alertText.textContent = message;

        if (type === ALERT_SUCCESS || type === ALERT_ERROR) {
            setTimeout(() => {
                elements.alertBox.className = 'terminal-alert';
                elements.alertText.textContent = 'Ready';
            }, 3000);
        }
    }

    async function apiRequest(endpoint, options = {}) {
        try {
            const response = await fetch(`/media_ccc${endpoint}`, {
                method: options.method || 'GET',
                headers: {
                    'Content-Type': 'application/json',
                    ...options.headers
                },
                body: options.body ? JSON.stringify(options.body) : undefined
            });

            if (!response.ok) {
                const errorText = await response.text();
                throw new Error(`HTTP ${response.status}: ${errorText}`);
            }

            return await response.json();
        } catch (error) {
            console.error('API request failed:', error);
            throw error;
        }
    }

    async function loadPreferences() {
        showAlert(ALERT_LOADING, 'Fetching language preferences...');

        try {
            const [audioLanguages, subtitleLanguages] = await Promise.all([
                apiRequest('/languages/audio'),
                apiRequest('/languages/subtitles')
            ]);

            state.audioLanguages = audioLanguages || [];
            state.subtitleLanguages = subtitleLanguages || [];

            renderLists();
            showAlert(ALERT_SUCCESS, 'Language preferences loaded');
        } catch (error) {
            showAlert(ALERT_ERROR, `Failed to load preferences: ${error.message}`);
        }
    }

    async function savePreferences(e) {
        e.preventDefault();
        showAlert(ALERT_LOADING, 'Saving preferences...');

        try {
            await Promise.all([
                apiRequest('/languages/audio', {
                    method: 'POST',
                    body: state.audioLanguages
                }),
                apiRequest('/languages/subtitles', {
                    method: 'POST',
                    body: state.subtitleLanguages
                })
            ]);

            showAlert(ALERT_SUCCESS, 'Preferences saved successfully');
        } catch (error) {
            showAlert(ALERT_ERROR, `Failed to save preferences: ${error.message}`);
        }
    }

    function addLanguage(type) {
        const select = type === 'audio' ? elements.audioSelect : elements.subtitleSelect;
        const code = select.value;

        if (!code) {
            showAlert(ALERT_ERROR, 'Please select a language first');
            return;
        }

        const list = type === 'audio' ? state.audioLanguages : state.subtitleLanguages;
        
        if (list.includes(code)) {
            showAlert(ALERT_ERROR, 'Language already in list');
            return;
        }

        list.push(code);
        renderLists();
        select.value = '';
        showAlert(ALERT_SUCCESS, `Added ${LANGUAGE_NAMES[code]} to preferences`);
    }

    function removeLanguage(type, code) {
        if (type === 'audio') {
            state.audioLanguages = state.audioLanguages.filter(l => l !== code);
        } else {
            state.subtitleLanguages = state.subtitleLanguages.filter(l => l !== code);
        }
        renderLists();
    }

    function renderLists() {
        renderLanguageList('audio', state.audioLanguages);
        renderLanguageList('subtitle', state.subtitleLanguages);
    }

    function renderLanguageList(type, languages) {
        const listElement = type === 'audio' ? elements.audioList : elements.subtitleList;

        if (languages.length === 0) {
            listElement.innerHTML = '';
            listElement.classList.add('empty');
            return;
        }

        listElement.classList.remove('empty');
        listElement.innerHTML = languages.map((code, index) => {
            const name = LANGUAGE_NAMES[code] || code;
            return `
                <li class="language-item" 
                    data-code="${code}" 
                    data-index="${index}"
                    draggable="true">
                    <span class="drag-handle">
                        <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                            <line x1="8" y1="6" x2="16" y2="6"></line>
                            <line x1="8" y1="12" x2="16" y2="12"></line>
                            <line x1="8" y1="18" x2="16" y2="18"></line>
                        </svg>
                    </span>
                    <div>
                        <span class="language-name">${escapeHtml(name)}</span>
                        <span class="language-code">${escapeHtml(code)}</span>
                    </div>
                    <button type="button" class="remove-btn" data-type="${type}" data-code="${code}">
                        Remove
                    </button>
                </li>
            `;
        }).join('');

        attachDragListeners(listElement, type);
        attachRemoveListeners(listElement);
    }

    function attachDragListeners(listElement, type) {
        const items = listElement.querySelectorAll('.language-item');

        items.forEach(item => {
            item.addEventListener('dragstart', handleDragStart);
            item.addEventListener('dragend', handleDragEnd);
            item.addEventListener('dragover', handleDragOver);
            item.addEventListener('dragleave', handleDragLeave);
            item.addEventListener('drop', (e) => handleDrop(e, type));
        });
    }

    function attachRemoveListeners(listElement) {
        const buttons = listElement.querySelectorAll('.remove-btn');

        buttons.forEach(btn => {
            btn.addEventListener('click', (e) => {
                e.preventDefault();
                const type = btn.dataset.type;
                const code = btn.dataset.code;
                removeLanguage(type, code);
            });
        });
    }

    function handleDragStart(e) {
        state.draggedItem = e.target.closest('.language-item');
        state.draggedItem.classList.add('dragging');
        e.dataTransfer.effectAllowed = 'move';
    }

    function handleDragEnd(e) {
        if (state.draggedItem) {
            state.draggedItem.classList.remove('dragging');
            state.draggedItem = null;
        }

        document.querySelectorAll('.language-item').forEach(item => {
            item.classList.remove('drag-over');
        });
    }

    function handleDragOver(e) {
        e.preventDefault();
        e.dataTransfer.dropEffect = 'move';

        const targetItem = e.target.closest('.language-item');
        if (targetItem && targetItem !== state.draggedItem) {
            targetItem.classList.add('drag-over');
        }
    }

    function handleDragLeave(e) {
        const targetItem = e.target.closest('.language-item');
        if (targetItem) {
            targetItem.classList.remove('drag-over');
        }
    }

    function handleDrop(e, type) {
        e.preventDefault();

        const targetItem = e.target.closest('.language-item');
        if (!targetItem || !state.draggedItem) return;

        targetItem.classList.remove('drag-over');

        const fromIndex = parseInt(state.draggedItem.dataset.index, 10);
        const toIndex = parseInt(targetItem.dataset.index, 10);

        if (fromIndex === toIndex) return;

        const list = type === 'audio' ? state.audioLanguages : state.subtitleLanguages;
        const [removed] = list.splice(fromIndex, 1);
        list.splice(toIndex, 0, removed);

        renderLists();
        showAlert(ALERT_SUCCESS, 'Language order updated');
    }

    function escapeHtml(text) {
        const div = document.createElement('div');
        div.textContent = text;
        return div.innerHTML;
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();