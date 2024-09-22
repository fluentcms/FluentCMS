
function onEditButtonClicked() {
    window.location.href = window.location.href + '?pageEdit=true'
}

export async function initialize(dotnet, togglerEl, buttonsEl) {
    function OnToggle(event) {
        togglerEl.classList.toggle('f-floating-button-toggler-open')
        buttonsEl.classList.toggle('f-floating-buttons-open')
    }

    togglerEl.addEventListener('click', OnToggle)
    buttonsEl.querySelector('#editButton').addEventListener('click', onEditButtonClicked)
}