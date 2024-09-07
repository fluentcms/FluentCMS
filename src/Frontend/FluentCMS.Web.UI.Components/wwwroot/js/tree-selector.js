export function TreeSelector(el, { cssPrefix = 'tree', options = [], onChange } = {}) {
    const menu = document.createElement('div')
    const input = document.createElement('input')

    function dispose() {
        console.log('Should dispose tree selector')
    }

    function getValue() {
        return el.dataset.value
    }

    function toggleValue(value) {
        if(getValue() === value) {
            setValue(null)
        } else {
            setValue(value)
        }
    }
    function setValue(value) {
        if(onChange) {
            onChange(value)
        }
        el.querySelectorAll('.' + cssPrefix + '-option-active').forEach(el => el.classList.remove(cssPrefix + '-option-active'))
        if(!value) {
            delete el.dataset.value;
        } else {
            el.dataset.value = value
            el.querySelector(`[data-value="${value}"]`).classList.add(cssPrefix + '-option-active')
        }
    }
    
    function toggleCollapsible(optionEl) {
        optionEl.querySelector('.' + cssPrefix + '-menu-inner').classList.toggle(cssPrefix + '-menu-inner-open')
    }

    function setInnerOptions(options, menuEl) {
        menuEl.childNodes.forEach(el => el.remove())
        
        for(let option of options) {
            const optionEl = document.createElement('div')
            optionEl.dataset.value = option.key
            optionEl.classList.add(cssPrefix + '-option')
            optionEl.innerHTML = `
            <div class="${cssPrefix}-option-header">
                ${option.items  ? `
                    <div class="${cssPrefix}-option-toggler">
                        <span class="${cssPrefix}-option-toggler-icon"></span>
                    </div>
                ` : `
                    <div class="${cssPrefix}-option-toggler ${cssPrefix}-option-toggler-hidden"></div>
                `}
                <div class="${cssPrefix}-option-content">
                    <div class="${cssPrefix}-option-icon">
                        ${option.icon}
                    </div>
                    <div class="${cssPrefix}-option-text">
                        ${option.text}
                    </div>
                </div>
                </div>
                ${option.items  ? `
                    <div class="${cssPrefix}-menu-inner">
                ` : ''}
            `
            menuEl.appendChild(optionEl)

            
            setTimeout(() => {
                if(option.items) {
                    const menuEl = optionEl.querySelector('.' + cssPrefix + '-menu-inner')
                    setInnerOptions(option.items, menuEl)
                }
                if(option.items) {
                    optionEl.querySelector(`.${cssPrefix}-option-toggler`).addEventListener('click', () => {
                        toggleCollapsible(optionEl)
                    })
                }

                optionEl.querySelector(`.${cssPrefix}-option-content`).addEventListener('click', () => {
                    toggleValue(optionEl.dataset.value)
                })
            })
        }
    }

    function setOptions(items) {
        setInnerOptions(items, menu)
    }

    el.classList.add(cssPrefix + '-wrapper')
    
    input.readOnly = true
    input.classList.add(cssPrefix + '-input')

    input.addEventListener('click', (ev) => {
        ev.preventDefault()
        openMenu()
    })

    el.appendChild(input)


    menu.classList.add(cssPrefix + '-menu')

    document.addEventListener('click', (e) => {
        if (!input.contains(e.target) && !menu.contains(e.target)) {
            closeMenu()
        }
    })

    function openMenu() {
        menu.classList.add(cssPrefix + '-menu-open')
    }
    function closeMenu() {
        menu.classList.remove(cssPrefix + '-menu-open')
    }

    el.appendChild(menu)

    setOptions(options)
    return {
        setOptions,
        setValue,
        getValue,
        dispose
    }
} 