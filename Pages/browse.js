(() => {

    const apiClient = window.ApiClient;
    const state = { conferences: [], filteredConferences: [], searchTerm: '', yearFilter: '' };

    function apiRequest(path, options) {
        const request = options || {};
        return fetch(`${apiClient.serverAddress()}/media_ccc${path}`, {
            method: request.method || 'GET',
            headers: {
                Authorization: `MediaBrowser Token="${apiClient.accessToken()}"`,
                'Content-Type': 'application/json'
            },
            body: request.body ? JSON.stringify(request.body) : undefined
        }).then((response) => {
            if (!response.ok) {
                throw new Error(`HTTP ${response.status}`);
            }
            return response.status === 204 ? null : response.json();
        });
    }

    function init() {
        document.getElementById('conferenceSearch').addEventListener('input', (event) => {
            state.searchTerm = event.target.value.toLowerCase();
            filterConferences();
        });
        document.getElementById('yearFilter').addEventListener('change', (event) => {
            state.yearFilter = event.target.value;
            filterConferences();
        });
        loadConferences();
    }

    function loadConferences() {
        const grid = document.getElementById('conferenceGrid');
        grid.textContent = 'Loading conferences...';
        apiRequest('/conferences')
            .then((conferences) => {
                state.conferences = conferences || [];
                state.filteredConferences = state.conferences.slice();
                populateYearFilter();
                renderConferences();
            })
            .catch(() => {
                grid.textContent = 'Failed to load conferences. Please try again later.';
            });
    }

    function populateYearFilter() {
        const select = document.getElementById('yearFilter');
        select.textContent = '';
        const allYears = document.createElement('option');
        allYears.value = '';
        allYears.textContent = 'All Years';
        select.appendChild(allYears);

        const years = new Set(state.conferences
            .filter((conference) => conference.updatedAt)
            .map((conference) => new Date(conference.updatedAt).getFullYear()));
        Array.from(years).sort((left, right) => right - left).forEach((year) => {
            const option = document.createElement('option');
            option.value = String(year);
            option.textContent = String(year);
            select.appendChild(option);
        });
    }

    function filterConferences() {
        state.filteredConferences = state.conferences.filter((conference) => {
            const title = String(conference.title || '').toLowerCase();
            const acronym = String(conference.acronym || '').toLowerCase();
            const matchesSearch = !state.searchTerm || title.includes(state.searchTerm) || acronym.includes(state.searchTerm);
            const year = conference.updatedAt ? String(new Date(conference.updatedAt).getFullYear()) : '';
            return matchesSearch && (!state.yearFilter || year === state.yearFilter);
        });
        renderConferences();
    }

    function renderConferences() {
        const grid = document.getElementById('conferenceGrid');
        grid.textContent = '';
        if (state.filteredConferences.length === 0) {
            grid.textContent = 'No conferences found.';
            return;
        }

        state.filteredConferences.forEach((conference) => {
            const card = document.createElement('button');
            card.type = 'button';
            card.className = 'conference-card';
            card.addEventListener('click', () => loadEvents(conference));

            const image = document.createElement('img');
            image.src = `https://api.media.ccc.de/public/conferences/${encodeURIComponent(conference.acronym)}/poster`;
            image.alt = String(conference.acronym || conference.title || 'Conference');
            card.appendChild(image);

            const content = document.createElement('span');
            content.className = 'conference-card-content';
            const title = document.createElement('strong');
            title.textContent = String(conference.title || 'Untitled conference');
            const acronym = document.createElement('span');
            acronym.textContent = String(conference.acronym || '');
            content.append(title, acronym);
            card.appendChild(content);
            grid.appendChild(card);
        });
    }

    function loadEvents(conference) {
        const panel = document.getElementById('eventPanel');
        const heading = document.getElementById('eventHeading');
        const list = document.getElementById('eventList');
        panel.hidden = false;
        heading.textContent = String(conference.title || conference.acronym || 'Conference events');
        list.textContent = 'Loading events...';

        apiRequest(`/conferences/${encodeURIComponent(conference.acronym)}/events`)
            .then((events) => renderEvents(events || []))
            .catch(() => { list.textContent = 'Failed to load events.'; });
    }

    function renderEvents(events) {
        const list = document.getElementById('eventList');
        list.textContent = '';
        if (events.length === 0) {
            list.textContent = 'No events found.';
            return;
        }

        events.forEach((event) => {
            const row = document.createElement('div');
            row.className = 'event-row';
            const title = document.createElement('span');
            title.textContent = String(event.title || event.guid || 'Untitled event');
            const button = document.createElement('button');
            button.type = 'button';
            button.textContent = 'Add to watchlist and download';
            button.addEventListener('click', () => enqueueEvent(event.guid, button));
            row.append(title, button);
            list.appendChild(row);
        });
    }

    function enqueueEvent(eventGuid, button) {
        button.disabled = true;
        Promise.resolve()
            .then(() => apiRequest(`/watchlist/${encodeURIComponent(eventGuid)}`, { method: 'POST' }))
            .then(() => apiRequest(`/downloads/${encodeURIComponent(eventGuid)}`, { method: 'POST' }))
            .then(() => { button.textContent = 'Queued'; })
            .catch(() => {
                button.disabled = false;
                button.textContent = 'Retry download';
            });
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
