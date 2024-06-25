document.querySelectorAll('a').forEach(el => {
    el.setAttribute('disabled', '')
    el.setAttribute('href', '')
})

document.querySelectorAll('.f-button').forEach(el => {
    el.setAttribute('disabled', '')
})