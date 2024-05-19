var themeToggleDarkIcon = document.getElementById('theme-toggle-dark-icon');
var themeToggleLightIcon = document.getElementById('theme-toggle-light-icon');

var themeToggleBtn = document.getElementById('theme-toggle');

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
    initTheme() // defined in app.head.js
    updateThemeIcon()
    themeToggleBtn.addEventListener('click', onToggleThemeBtnClicked);
}

function destroy() {
    themeToggleBtn.removeEventListener('click', onToggleThemeBtnClicked);
}

window.addEventListener('fluentcms:beforeenhanced', () => {
    destroy()
})

window.addEventListener('fluentcms:afterenhanced', () => {
    initialize()
})

window.addEventListener('fluentcms:init', () => {
    initialize()
})