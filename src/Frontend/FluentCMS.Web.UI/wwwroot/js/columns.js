export function Columns(element, {gridLines = true, colClass = 'col', breakpointMd = 480, breakpointLg = 992, onResize} = {}) {
    let windowWidth = window.innerWidth;
    let oneColWidth = element.clientWidth / 12;

    function initColumn(el) {
        let resizer;
        let dragging = false
        let x = 0;
        let field = 'cols'
        let value = el.dataset[field]
        let resized = false

        if(isNaN(el.dataset.cols) || el.dataset.cols == 0) {
            el.dataset.cols = 12
        }

        function onMouseDown(event) {
            resized = false

            x = event.x;
            dragging = true
            element.classList.add('dragging')
            resizer.classList.add('dragging')
        }

        function onMouseMove(event) {
            if(dragging) {

                // if(windowWidth > breakpointLg) {
                //     field = 'colsLg'
                // } else if(windowWidth > breakpointMd) {
                //     field = 'colsMd'
                // }
                // value = el.dataset[field]


                const diffLength = event.x - x
                
                if(diffLength < -oneColWidth/2 || diffLength > oneColWidth / 2) {
                    if(el.dataset[field] == 0) {
                        if(field == 'colsLg' && el.dataset['colsMd']) {
                            el.dataset[field] = el.dataset['cols']
                        } 
                        el.dataset[field] = el.dataset['cols']
                    }
                    if(diffLength < -oneColWidth / 2) {
                        el.dataset[field] = +(el.dataset[field]) - 1
                        x = x - oneColWidth
                    } else {
                        el.dataset[field] = +(el.dataset[field]) + 1
                        x = x + oneColWidth
                    }
                }
            }
        }

        function onMouseUp(event) {
            resizer.style.right = '-24px'
            dragging = false

            if(!resized && onResize && value !== el.dataset[field]) {
                resized = true

                onResize({field, value})
            }

            element.classList.remove('dragging')
            resizer.classList.remove('dragging')
        }

        function init() {
            resizer = document.createElement('div')
            resizer.classList.add('resizer-handle')
            el.appendChild(resizer)

            resizer.addEventListener('mousedown', onMouseDown)
            document.addEventListener('mousemove', onMouseMove)
            document.addEventListener('mouseup', onMouseUp)
        }

        function destroy() {
            resizer.removeEventListener('mousedown', onMouseDown)
            document.removeEventListener('mousemove', onMouseMove)
            document.removeEventListener('mouseup', onMouseUp)

            resizer.remove()
        }

        init()
        return { init, destroy }
    }

    let columns = []

    function updateSize() {
        windowWidth = window.innerWidth;
        oneColWidth = element.clientWidth / 12

        if(gridLines) {
            element.querySelectorAll('.line').forEach((el, index) => {
                el.style.left = (oneColWidth * index + 1) + 'px'
            })
        }
    }

    function init() {
        if(gridLines) {
            for(let i=0; i<12; i++) {
                const line = document.createElement('div')
                line.classList.add('line')
                line.style.left = (oneColWidth * i) + 'px'

                element.appendChild(line)
            }
        }
        element.dataset.columns = ''
        element.classList.add('active')

        element.querySelectorAll('.' + colClass).forEach(el => {

            columns.push(initColumn(el))
        })  
        window.addEventListener('resize', updateSize)
    }

    function destroy() {
        element.classList.remove('active')
        if(gridLines) {
            element.querySelectorAll('.line').forEach(el => el.remove())
        }
        columns.map(column => column.destroy())
        columns = []
        window.removeEventListener('resize', updateSize)
    }

    function append(el) {
        setTimeout(() => {
            columns.push(initColumn(el))
        })
    }

    return {
        init, 
        destroy, 
        append,
        updateSize
    }
}

window.Columns = Columns