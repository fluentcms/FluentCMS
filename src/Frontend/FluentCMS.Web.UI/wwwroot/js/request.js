import './sortable.js'

import {actions} from './actions.js'
import { createPlugin, updatePluginCols, updatePluginOrders } from './api.js';
import {Columns} from './columns.js'

export function initColumns() {
    for(let section in window.sections ?? {}) {
        window.sections[section].destroy()
    }
    window.sections = {}
    
    document.querySelectorAll('.f-section').forEach(section => {
        const column = new Columns(section, {
            doc: document,
            gridLines: true,
            colClass: 'f-plugin-container',
            breakpointLg: 992,
            breakpointMd: 480,
            onResize(el) {
                updatePluginCols(el, section)
            }
        })
        
        setTimeout(() => {
            column.init()
        }, 100)
        window.sections[section.dataset.name] = column
    })
}

export function closePluginsSidebar() {
    
    document.querySelector('.f-page-editor-sidebar').classList.remove('f-page-editor-sidebar-open')
}

export function initializeSortable() {
    const sectionElements = document.querySelectorAll('.f-section');

    sectionElements.forEach(section => {
        new Sortable(section, {
            animation: 150,
            direction: 'vertical',
            group: 'shared',
            draggable: '.f-plugin-container',
            ghostClass: 'f-plugin-container-moving',
            chosenClass: 'f-plugin-container-chosen',
            handle: '.f-plugin-container-action-drag',
            onEnd() {
                updatePluginOrders()
            }
        });
    });

    new Sortable(document.querySelector('.f-plugin-definition-list'), {
        animation: 150,
        group: {
            name: 'shared',
            pull: 'clone',
            put: false
        },
        sort: false,
        draggable: '.f-plugin-definition-item',
        onEnd(event) {
            if(event.from === event.to) return;
            const definitionId = event.clone.dataset.id
            const item = event.item
            const order = (event.newIndex * 2) + 1
            const sectionName = event.to.dataset.name

            item.remove()
            createPlugin({ definitionId, order, sectionName })
        }
    });
}

export async function hydrate(element) {
    element.querySelectorAll('[data-action]').forEach(action => {
        if(action.dataset.trigger === 'load') {
            actions[action.dataset.action](action)
        } else {
            action.addEventListener('click', () => {
                actions[action.dataset.action](action)
            })
        }
    })
}