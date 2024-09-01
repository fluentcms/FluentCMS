﻿import { initializeBlocks } from './blocks.js';
import { hydrate, initializeSortable, initializeInlineEditables, initColumns } from './request.js'
import { initializeResponsive } from './responsive.js'

let iframeElement = document.querySelector('.f-page-editor-iframe')

let frameDocument;
function getFrameDocument() {
    return new Promise(resolve => {
        iframeElement.onload = () => {
            frameDocument = iframeElement.contentDocument;
            resolve(frameDocument)
        }
    })
}

async function onInit() {
    const frameDocument = await getFrameDocument()

    initializeResponsive()
    initializeBlocks()

    hydrate(frameDocument)
    hydrate(document)
    initializeSortable(frameDocument)
    initializeInlineEditables(frameDocument)
    initColumns(frameDocument)
}

document.addEventListener('fluentcms:afterenhanced', () => {
    onInit()
})

onInit()