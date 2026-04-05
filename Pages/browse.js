(function() {
    'use strict';

    const ApiClient = window.ApiClient;
    
    const state = {
        conferences: [],
        filteredConferences: [],
        searchTerm: '',
        yearFilter: ''
    };

    function init() {
        loadConferences();
        setupEventListeners();
    }

    async function loadConferences() {
        const grid = document.getElementById('conferenceGrid');
        grid.innerHTML = '<div class="loading">Loading conferences...</div>';

        try {
            const response = await ApiClient.fetch({
                url: 'media_ccc/conferences',
                type: 'GET'
            });

            state.conferences = response || [];
            state.filteredConferences = [...state.conferences];
            
            populateYearFilter();
            renderConferences();
        } catch (error) {
            console.error('Failed to load conferences:', error);
            grid.innerHTML = '<div class="error">Failed to load conferences. Please try again later.</div>';
        }
    }

    function populateYearFilter() {
        const yearFilter = document.getElementById('yearFilter');
        const years = new Set();
        
        state.conferences.forEach(conference => {
            if (conference.updatedAt) {
                const year = new Date(conference.updatedAt).getFullYear();
                years.add(year);
            }
        });

        const sortedYears = Array.from(years).sort((a, b) => b - a);
        
        yearFilter.innerHTML = '<option value="">All Years</option>';
        sortedYears.forEach(year => {
            const option = document.createElement('option');
            option.value = year;
            option.textContent = year;
            yearFilter.appendChild(option);
        });
    }

    function setupEventListeners() {
        const searchInput = document.getElementById('conferenceSearch');
        const yearFilter = document.getElementById('yearFilter');

        searchInput.addEventListener('input', (e) => {
            state.searchTerm = e.target.value.toLowerCase();
            filterConferences();
        });

        yearFilter.addEventListener('change', (e) => {
            state.yearFilter = e.target.value;
            filterConferences();
        });
    }

    function filterConferences() {
        state.filteredConferences = state.conferences.filter(conference => {
            const matchesSearch = !state.searchTerm || 
                conference.title.toLowerCase().includes(state.searchTerm) ||
                conference.acronym.toLowerCase().includes(state.searchTerm);

            const matchesYear = !state.yearFilter || 
                (conference.updatedAt && new Date(conference.updatedAt).getFullYear().toString() === state.yearFilter);

            return matchesSearch && matchesYear;
        });

        renderConferences();
    }

    function renderConferences() {
        const grid = document.getElementById('conferenceGrid');
        
        if (state.filteredConferences.length === 0) {
            grid.innerHTML = '<div class="no-results">No conferences found</div>';
            return;
        }

        grid.innerHTML = state.filteredConferences.map(conference => {
            const posterUrl = conference.url ? 
                `https://api.media.ccc.de/public/conferences/${conference.id}/poster` :
                '';
            
            const dateStr = conference.updatedAt ? 
                new Date(conference.updatedAt).toLocaleDateString() : 
                '';

            return `
                <div class="conference-card" data-conference-id="${conference.id}">
                    <img src="${posterUrl}" alt="${conference.acronym}" 
                         onerror="this.src='data:image/svg+xml,%3Csvg xmlns=%22http://www.w3.org/2000/svg%22 width=%22280%22 height=%22160%22%3E%3Crect fill=%22%232a2a2a%22 width=%22280%22 height=%22160%22/%3E%3Ctext fill=%22%23888%22 x=%22140%22 y=%2280%22 text-anchor=%22middle%22 font-size=%2220%22%3E${conference.acronym}%3C/text%3E%3C/svg%3E'">
                    <div class="conference-card-content">
                        <h3>${escapeHtml(conference.title)}</h3>
                        <p class="subtitle">${escapeHtml(conference.acronym)}</p>
                        ${dateStr ? `<span class="date">${dateStr}</span>` : ''}
                    </div>
                </div>
            `;
        }).join('');

        grid.querySelectorAll('.conference-card').forEach(card => {
            card.addEventListener('click', () => {
                const conferenceId = card.dataset.conferenceId;
                if (conferenceId) {
                    navigateToEvents(conferenceId, state.conferences.find(c => c.id == conferenceId));
                }
            });
        });
    }

    function navigateToEvents(conferenceId, conference) {
        const url = `media_ccc/events/${conferenceId}`;
        if (conference) {
            Dashboard.navigate(url + '?title=' + encodeURIComponent(conference.title));
        } else {
            Dashboard.navigate(url);
        }
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