let theme = null;
let sidebar = null;

window.addEventListener('fluentcms:beforeenhanced', () => {
    if(theme) theme.destroy()
    if(sidebar) sidebar.destroy()
})

window.addEventListener('fluentcms:afterenhanced', () => {
    // update elements after page navigated
    theme = createTheme()
    sidebar = createSidebar()
    
    if(theme) theme.initialize()
    if(sidebar) sidebar.initialize()

    initFlowbite()
})

window.addEventListener('fluentcms:init', () => {
    theme = createTheme();
    sidebar = createSidebar();

    if(theme) theme.initialize()
    if(sidebar) sidebar.initialize()
})

function createSidebar() {
    const isSidebarExpanded = toggleSidebarEl => {
        return toggleSidebarEl.getAttribute('aria-expanded') === 'true' ? true : false;
    }

    const toggleSidebar = (sidebarEl, expand, setExpanded = false) => {
        const mainContentEl = document.getElementById('main-content');
        if (expand) {
            sidebarEl.classList.add('lg:w-64');
            sidebarEl.classList.remove('lg:w-16');
            mainContentEl.classList.add('lg:ml-64');
            mainContentEl.classList.remove('lg:ml-16');

            document.querySelectorAll('#' + sidebarEl.getAttribute('id') + ' [sidebar-toggle-item]').forEach(sidebarToggleEl => {
                sidebarToggleEl.classList.remove('lg:hidden');
                sidebarToggleEl.classList.remove('lg:absolute');
            });

            // toggle multi level menu item initial and full text
            document.querySelectorAll('#' + sidebar.getAttribute('id') + ' ul > li > ul > li > a').forEach(e => {
                e.classList.add('pl-11');
                e.classList.remove('px-4');
                e.childNodes[0].classList.remove('hidden');
                e.childNodes[1].classList.add('hidden');
            });

            setExpanded ? toggleSidebarEl.setAttribute('aria-expanded', 'true') : null;
        } else {
            sidebarEl.classList.remove('lg:w-64');
            sidebarEl.classList.add('lg:w-16');
            mainContentEl.classList.remove('lg:ml-64');
            mainContentEl.classList.add('lg:ml-16');
            document.querySelectorAll('#' + sidebarEl.getAttribute('id') + ' [sidebar-toggle-item]').forEach(sidebarToggleEl => {
                sidebarToggleEl.classList.add('lg:hidden');
                sidebarToggleEl.classList.add('lg:absolute');
            });

            // toggle multi level menu item initial and full text
            document.querySelectorAll('#' + sidebar.getAttribute('id') + ' ul > li > ul > li > a').forEach(e => {
                e.classList.remove('pl-11');
                e.classList.add('px-4');
                e.childNodes[0].classList.add('hidden');
                e.childNodes[1].classList.remove('hidden');
            });

            setExpanded ? toggleSidebarEl.setAttribute('aria-expanded', 'false') : null;
        }
    }

    const toggleSidebarEl = document.getElementById('toggleSidebar');
    const sidebar = document.getElementById('sidebar');

    if(!sidebar) {
        return null;
    }

    document.querySelectorAll('#' + sidebar.getAttribute('id') + ' ul > li > ul > li > a').forEach(e => {
        var fullText = e.textContent;
        var firstLetter = fullText.substring(0, 1);

        var fullTextEl = document.createElement('span');
        var firstLetterEl = document.createElement('span');
        firstLetterEl.classList.add('hidden');
        fullTextEl.textContent = fullText;
        firstLetterEl.textContent = firstLetter;

        e.textContent = '';
        e.appendChild(fullTextEl);
        e.appendChild(firstLetterEl);
    });

    // initialize sidebar
    if (localStorage.getItem('sidebarExpanded') !== null) {
        if (localStorage.getItem('sidebarExpanded') === 'true') {
            toggleSidebar(sidebar, true, false);
        } else {
            toggleSidebar(sidebar, false, true);
        }
    }

    function onToggleSidebarElClicked() {
        localStorage.setItem('sidebarExpanded', !isSidebarExpanded(toggleSidebarEl));
        toggleSidebar(sidebar, !isSidebarExpanded(toggleSidebarEl), true);
    }

    function onSidebarMouseEntered() {
        if (!isSidebarExpanded(toggleSidebarEl)) {
            toggleSidebar(sidebar, true);
        }
    }

    function onSidebarMouseLeaved() {
        if (!isSidebarExpanded(toggleSidebarEl)) {
            toggleSidebar(sidebar, false);
        }
    }

    const toggleSidebarMobile = (sidebar, sidebarBackdrop, toggleSidebarMobileHamburger, toggleSidebarMobileClose) => {
        sidebar.classList.toggle('hidden');
        sidebarBackdrop.classList.toggle('hidden');
        toggleSidebarMobileHamburger.classList.toggle('hidden');
        toggleSidebarMobileClose.classList.toggle('hidden');
    }

    const toggleSidebarMobileEl = document.getElementById('toggleSidebarMobile');
    const sidebarBackdrop = document.getElementById('sidebarBackdrop');
    const toggleSidebarMobileHamburger = document.getElementById('toggleSidebarMobileHamburger');
    const toggleSidebarMobileClose = document.getElementById('toggleSidebarMobileClose');

    function onToggleSidebarMobileElClicked() {
        toggleSidebarMobile(sidebar, sidebarBackdrop, toggleSidebarMobileHamburger, toggleSidebarMobileClose);
    }

    function onSidebarBackdropClicked() {
        toggleSidebarMobile(sidebar, sidebarBackdrop, toggleSidebarMobileHamburger, toggleSidebarMobileClose);
    }

    function initialize() {
        toggleSidebarEl.addEventListener('click', onToggleSidebarElClicked);
        sidebar.addEventListener('mouseenter', onSidebarMouseEntered);
        sidebar.addEventListener('mouseleave', onSidebarMouseLeaved);
        toggleSidebarMobileEl.addEventListener('click', onToggleSidebarMobileElClicked);
        sidebarBackdrop.addEventListener('click', onSidebarBackdropClicked);
    }

    function destroy() {
        toggleSidebarEl.removeEventListener('click', onToggleSidebarElClicked);
        sidebar.removeEventListener('mouseenter', onSidebarMouseEntered);
        sidebar.removeEventListener('mouseleave', onSidebarMouseLeaved);
        toggleSidebarMobileEl.removeEventListener('click', onToggleSidebarMobileElClicked);
        sidebarBackdrop.removeEventListener('click', onSidebarBackdropClicked);
    }

    return {
        initialize,
        destroy
    }
}

function createTheme() {
    var themeToggleDarkIcon = document.getElementById('theme-toggle-dark-icon');
    var themeToggleLightIcon = document.getElementById('theme-toggle-light-icon');

    var themeToggleBtn = document.getElementById('theme-toggle');

    if(!themeToggleBtn) {
        return null;
    }
    
    function initTheme() {
        if (localStorage.getItem('color-theme') === 'dark' || (!('color-theme' in localStorage) && window.matchMedia('(prefers-color-scheme: dark)').matches)) {
            document.documentElement.classList.add('dark');
        } else {
            document.documentElement.classList.remove('dark')
        }
    }
    
    let event = new Event('dark-mode');

    function onToggleThemeBtnClicked() {

        // if set via local storage previously
        if (localStorage.getItem('color-theme')) {
            if (localStorage.getItem('color-theme') === 'light') {
                document.documentElement.classList.add('dark');
                localStorage.setItem('color-theme', 'dark');
            } else {
                document.documentElement.classList.remove('dark');
                localStorage.setItem('color-theme', 'light');
            }

            // if NOT set via local storage previously
        } else {
            if (document.documentElement.classList.contains('dark')) {
                document.documentElement.classList.remove('dark');
                localStorage.setItem('color-theme', 'light');
            } else {
                document.documentElement.classList.add('dark');
                localStorage.setItem('color-theme', 'dark');
            }
        }
        updateThemeIcon()
        document.dispatchEvent(event);
    }

    function updateThemeIcon() {
        setTimeout(() => {
            // Change the icons inside the button based on previous settings
            if (document.documentElement.classList.contains('dark')) {
                themeToggleDarkIcon.classList.add('hidden');
                themeToggleLightIcon.classList.remove('hidden');
            } else {
                themeToggleDarkIcon.classList.remove('hidden');
                themeToggleLightIcon.classList.add('hidden');
            }
        })
    }

    function initialize() {
        initTheme()
        updateThemeIcon()
        themeToggleBtn.addEventListener('click', onToggleThemeBtnClicked);
    }

    function destroy() {
        themeToggleBtn.removeEventListener('click', onToggleThemeBtnClicked);
    }

    return {
        initialize,
        destroy
    }
}
