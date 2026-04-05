(function() {
    const ALERT_LOADING = 'loading';
    const ALERT_SUCCESS = 'success';
    const ALERT_ERROR = 'error';

    const STATUS_STARTED = 'Started';
    const STATUS_COMPLETED = 'Completed';
    const STATUS_FAILED = 'Failed';

    const elements = {
        alertBox: null,
        alertText: null,
        syncLog: null,
        manualSyncBtn: null,
        refreshBtn: null,
        clearHistoryBtn: null
    };

    let syncHistory = [];

    function init() {
        elements.alertBox = document.getElementById('alertBox');
        elements.alertText = document.getElementById('alertText');
        elements.syncLog = document.getElementById('syncLog');
        elements.manualSyncBtn = document.getElementById('manualSync');
        elements.refreshBtn = document.getElementById('refreshLog');
        elements.clearHistoryBtn = document.getElementById('clearHistory');

        elements.manualSyncBtn.addEventListener('click', triggerManualSync);
        elements.refreshBtn.addEventListener('click', loadSyncHistory);
        elements.clearHistoryBtn.addEventListener('click', clearSyncHistory);

        loadSyncHistory();
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

    async function loadSyncHistory() {
        showAlert(ALERT_LOADING, 'Fetching sync history...');
        showLoadingSkeletons();

        try {
            const data = await apiRequest('/sync/history');
            syncHistory = data || [];
            renderSyncLog();
            showAlert(ALERT_SUCCESS, `Loaded ${syncHistory.length} sync entries`);
        } catch (error) {
            showAlert(ALERT_ERROR, `Failed to load sync history: ${error.message}`);
            showError(error.message);
        }
    }

    async function triggerManualSync() {
        elements.manualSyncBtn.disabled = true;
        showAlert(ALERT_LOADING, 'Triggering manual sync...');

        try {
            await apiRequest('/sync/trigger', { method: 'POST' });
            showAlert(ALERT_SUCCESS, 'Sync started successfully');

            setTimeout(() => {
                loadSyncHistory();
            }, 2000);
        } catch (error) {
            showAlert(ALERT_ERROR, `Failed to trigger sync: ${error.message}`);
        } finally {
            elements.manualSyncBtn.disabled = false;
        }
    }

    async function clearSyncHistory() {
        if (!confirm('Are you sure you want to clear all sync history? This cannot be undone.')) {
            return;
        }

        try {
            await apiRequest('/sync/history', { method: 'DELETE' });
            syncHistory = [];
            renderSyncLog();
            showAlert(ALERT_SUCCESS, 'Sync history cleared');
        } catch (error) {
            showAlert(ALERT_ERROR, `Failed to clear history: ${error.message}`);
        }
    }

    function renderSyncLog() {
        if (!elements.syncLog) return;

        if (syncHistory.length === 0) {
            elements.syncLog.innerHTML = `
                <div class="log-empty">
                    <div class="icon">📋</div>
                    <div>No sync history available</div>
                    <div style="font-size: 0.6875rem; margin-top: 0.5rem; opacity: 0.5;">
                        Trigger a manual sync to create the first entry
                    </</div>
                </div>
            `;
            return;
        }

        const entries = syncHistory
            .sort((a, b) => new Date(b.timestamp) - new Date(a.timestamp))
            .map((entry, index) => renderLogEntry(entry, index))
            .join('');

        elements.syncLog.innerHTML = entries;

        document.querySelectorAll('.log-header').forEach(header => {
            header.addEventListener('click', () => toggleEntryDetails(header));
        });
    }

    function renderLogEntry(entry, index) {
        const statusClass = getStatusClass(entry.status);
        const statusIcon = getStatusIcon(entry.status);
        const formattedTime = formatTimestamp(entry.timestamp);

        const duration = entry.status !== STATUS_STARTED
            ? formatDuration(entry.filesCreated)
            : 'In progress';

        return `
            <div class="log-entry" data-index="${index}">
                <div class="log-header">
                    <span class="log-status ${statusClass}">
                        ${statusIcon} ${entry.status}
                    </span>
                    <span class="log-conference">${escapeHtml(entry.conferenceAcronym || 'Unknown')}</span>
                    <span class="log-time">${formattedTime}</span>
                    <span class="expand-icon">▶</span>
                </div>
                <div class="log-details">
                    <div class="detail-grid">
                        <div class="detail-item">
                            <span class="detail-label">Events Processed</span>
                            <span class="detail-value">${entry.eventsProcessed || 0}</span>
                        </div>
                        <div class="detail-item">
                            <span class="detail-label">Files Created</span>
                            <span class="detail-value">${entry.filesCreated || 0}</span>
                        </div>
                        <div class="detail-item">
                            <span class="detail-label">Duration</span>
                            <span class="detail-value">${duration}</span>
                        </div>
                        ${entry.errorMessage ? `
                            <div class="detail-item">
                                <span class="detail-label">Error</span>
                                <span class="detail-value error">${escapeHtml(entry.errorMessage)}</span>
                            </div>
                        ` : ''}
                    </div>
                </div>
            </div>
        `;
    }

    function getStatusClass(status) {
        switch (status) {
            case STATUS_COMPLETED: return 'status-completed';
            case STATUS_FAILED: return 'status-failed';
            case STATUS_STARTED: return 'status-started';
            default: return '';
        }
    }

    function getStatusIcon(status) {
        switch (status) {
            case STATUS_COMPLETED: return '✓';
            case STATUS_FAILED: return '✗';
            case STATUS_STARTED: return '◐';
            default: return '?';
        }
    }

    function formatTimestamp(timestamp) {
        const date = new Date(timestamp);
        return date.toLocaleString('en-US', {
            year: 'numeric',
            month: '2-digit',
            day: '2-digit',
            hour: '2-digit',
            minute: '2-digit',
            second: '2-digit',
            hour12: false
        });
    }

    function formatDuration(filesCreated) {
        if (!filesCreated || filesCreated === 0) {
            return 'No files';
        }
        return `${filesCreated} file${filesCreated !== 1 ? 's' : ''}`;
    }

    function toggleEntryDetails(header) {
        const entry = header.closest('.log-entry');
        entry.classList.toggle('expanded');
    }

    function showLoadingSkeletons() {
        if (!elements.syncLog) return;

        elements.syncLog.innerHTML = `
            <div class="skeleton skeleton-entry"></div>
            <div class="skeleton skeleton-entry"></div>
            <div class="skeleton skeleton-entry"></div>
        `;
    }

    function showError(message) {
        if (!elements.syncLog) return;

        elements.syncLog.innerHTML = `
            <div class="log-empty" style="color: var(--accent-red);">
                <div class="icon">⚠</div>
                <div>Failed to load sync history</div>
                <div style="font-size: 0.6875rem; margin-top: 0.5rem; opacity: 0.7;">
                    ${escapeHtml(message)}
                </div>
            </div>
        `;
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