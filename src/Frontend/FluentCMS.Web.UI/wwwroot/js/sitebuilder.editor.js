import {onInit} from './init.js'

onInit()

window.addEventListener('fluentcms:afterenhanced', () => {
    onInit()
})
