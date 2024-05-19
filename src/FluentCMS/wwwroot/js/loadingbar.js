const hideLoadingBar = () => {
    const loadingBar = document.getElementById('loading-bar');
    if (loadingBar) {
        loadingBar.classList.remove('h-1')
        loadingBar.classList.add('h-0')
    }
}

const showLoadingBar = () => {
    const loadingBar = document.getElementById('loading-bar');
    if (loadingBar) {
        loadingBar.classList.remove('h-0')
        loadingBar.classList.add('h-1')
    }
}

window.addEventListener('fluentcms:beforeenhanced', () => {
    console.log('fluentcms:beforeenhanced')
    showLoadingBar()
})

window.addEventListener('fluentcms:afterenhanced', () => {
    console.log('fluentcms:afterenhanced')
    hideLoadingBar()
})

window.addEventListener('fluentcms:init', () => {
    console.log('fluentcms:init')
    hideLoadingBar()
})
