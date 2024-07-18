import {onInit} from './sitebuilder.js'

onInit()
console.log('sitebuilder.editor.js')

window.addEventListener('fluentcms:afterenhanced', () => {
    onInit()
})

// window.addEventListener('fluentcms:init', () => {
//     onInit()
// })
