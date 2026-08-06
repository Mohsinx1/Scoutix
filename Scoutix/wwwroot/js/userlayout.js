function setActiveSidebarLink(url) {
    const path = new URL(url, window.location.origin).pathname.toLowerCase();
    document.querySelectorAll('.nav-link').forEach(a => {
        const aPath = new URL(a.href, window.location.origin).pathname.toLowerCase();
        if (aPath === path) {
            a.classList.add('active');
        } else {
            a.classList.remove('active');
        }
    });
}
document.addEventListener('DOMContentLoaded', function () {
    setActiveSidebarLink(location.href);
    if (typeof initLeadsGeoFilters === 'function') {
        initLeadsGeoFilters();
        initNicheDropdown();
    }
    if (typeof initSimpleDropdowns === 'function') {
        initSimpleDropdowns();
    }
});
document.addEventListener('DOMContentLoaded', function () {
    const KEY = 'sidebarCollapsed';
    const root = document.documentElement;
    const btn = document.getElementById('sidebarToggle');
    const sidebar = document.getElementById('appSidebar');
    const overlay = document.getElementById('sidebarOverlay');

    function isMobile() {
        return window.innerWidth <= 1024;
    }

    function closeMobileSidebar() {
        if (sidebar) sidebar.classList.remove('mobile-open');
        if (overlay) overlay.classList.remove('active');
    }

    if (btn) {
        btn.addEventListener('click', () => {
            if (isMobile()) {
                const isOpen = sidebar.classList.toggle('mobile-open');
                overlay.classList.toggle('active', isOpen);
            } else {
                const collapsed = root.classList.toggle('sidebar-collapsed');
                localStorage.setItem(KEY, collapsed);
            }
        });
    }

    if (overlay) {
        overlay.addEventListener('click', closeMobileSidebar);
    }

    document.querySelectorAll('.app-sidebar .nav-link').forEach(link => {
        link.addEventListener('click', () => {
            if (isMobile()) closeMobileSidebar();
        });
    });
});