function onEditButtonClicked() {
    window.location.href = window.location.href + '?pageEdit=true'
}

function init() {
    if(window.editButton) {
        editButton.addEventListener('click', onEditButtonClicked)    
    }
}

function destroy () {
    if(window.editButton) {
        editButton.removeEventListener('click', onEditButtonClicked)    
    }
}

init()