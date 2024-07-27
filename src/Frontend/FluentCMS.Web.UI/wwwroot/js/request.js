import { lifecycle } from './lifecycle.js'

export async function request({handler, body, relaod: shouldReload = true}) {
    const url = window.location.href
    const formData = new FormData()
            
    formData.set('__RequestVerificationToken', document.querySelector('[name="__RequestVerificationToken"]').value)
    formData.set('_handler', handler)

    for(let key in body) {
        formData.set(key, body[key])
    }

    await fetch(url, {
        method: 'POST',
        body: formData
    })

    if(shouldReload)
        lifecycle.trigger('reload')
}
