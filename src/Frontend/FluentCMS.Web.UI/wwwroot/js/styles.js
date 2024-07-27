import { debounce, parseStyles } from "./helpers.js"

const mainTemplate = ({classes = { tableClass: 'f-style-editor-table'}}) => `<table class="${classes.tableClass}" cellspacing="0">
    <thead>
        <tr>
            <th><div style="height: 24px">Style Editor</div></th>
            <th><div style="height: 24px"></div></th>
            <th style="width: 0;">
                
            </th>
        </tr>
    </thead>
    <tbody>
        <tr data-style-actions>
            <td style="padding: 4px" colspan="3">
                <div class="f-style-editor-buttons">
                    <div data-style-action="add" class="f-style-editor-add-btn f-outline" style="display: flex; align-items: center; gap: 4px; width: max-content; padding-right: 16px;">
                        <svg style="width: 16px; height: 16px;" aria-hidden="true" xmlns="http://www.w3.org/2000/svg" width="24" height="24" fill="none" viewBox="0 0 24 24">
                            <path stroke="currentColor" stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M5 12h14m-7 7V5"/>
                        </svg>
                        <span>Add</span>
                    </div>
                </div>
            </td>
        </tr>
    </tbody>
</table>`

export function StyleManager(options = {}) {
    const onChange = options.onChange ?? null
    const frameDocument = options.frameDocument
    
    const selectors = options.selectors ?? {}
    const styleEditorSelector = '[data-style-editor]'
    const styleActionsSelector = '[data-style-actions]'
    const rowSelector = '[data-styles-row]'

    let styleEditorElement
    
    function init() {
        styleEditorElement = document.querySelector(styleEditorSelector);
        styleEditorElement.innerHTML = mainTemplate({
            classes: {
                tableClass: 'f-style-editor-table'
            }
        })

        styleEditorElement.querySelector('[data-style-action="add"]').addEventListener('click', () => {
            // 
            const content = getStyleEditorRow('', '')
            insertRow(content)
        })
    }


    function insertRow(content) {
        const styleEditorContent = document.createElement('div')
        
        
        setTimeout(() => {
            styleEditorContent.outerHTML = content

            setTimeout(() => {
                styleEditorElement.querySelectorAll('[data-style-row]').forEach(row => {
                    row.querySelector('[data-style-key]').addEventListener('input', handleChange)
                    row.querySelector('[data-style-value]').addEventListener('input', handleChange)
                    row.querySelector('[data-style-remove]').addEventListener('click', () => {
                        // remove current row
                        row.remove()
                        handleChange()
                    })
                })
            })
        })

        const actionsElement = styleEditorElement.querySelector(styleActionsSelector)
        actionsElement.parentElement.insertBefore(styleEditorContent, actionsElement)

    }

    function getStyleEditorRow(key, value) {
        const template = `<tr data-style-row>
            <td><div><input data-style-key placeholder="key" value="$0" /></div></td>
            <td><div><input data-style-value placeholder="value" value="$1" /></div></td>
            <td>
                <button data-style-remove class="f-style-editor-remove-btn"></button>
            </td>
        </tr>`

        return template.replace('$0', key).replace('$1', value)
    }

    const debouncedOnChange = debounce((args) => {
        if(onChange) {
            onChange(args[0])
        }
    }, 1000)

    function handleChange(event) {
        let styles = {}

        const element = frameDocument.getElementById(styleEditorElement.dataset.currentElementId)
        styleEditorElement.querySelectorAll('[data-style-row]').forEach(row => {
            const key = row.querySelector('[data-style-key]').value
            const value = row.querySelector('[data-style-value]').value

            if(key && value) {
                styles[key] = value
            }
        })

        let stylesStr = ''
        for(let style in styles) {
            if(style[0] > 'A' && style[0] < 'Z') {
                const key = style[0].toLowerCase() + style.slice(1)
                element.dataset[key] = styles[style]
            } else {
                stylesStr += `${style}: ${styles[style]};`
            }
        }
        element.style = stylesStr

        debouncedOnChange(element)
        // return 
    }

    function generateStyleContent(style = '', dataset = {}) {
        let styles = parseStyles(style, dataset)

        let result = ''
        for(let key in styles) {
            if(key) {
                result += getStyleEditorRow(key, styles[key])
            }
        }
    
        return result;
    }

    function open(element) {
        styleEditorElement.dataset.currentElementId = element.id
        const style = element.getAttribute('style')
        const dataset = element.dataset
        
        styleEditorElement.querySelectorAll('[data-style-row]').forEach(el => {
            el.remove()
        })
        
        insertRow(generateStyleContent(style, dataset))
    }

    function destroy() {
        document.querySelector(styleEditorSelector).innerHTML = ''
    }

    init()
    
    return {
        destroy,
        init,
        open,
    }
}