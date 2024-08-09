export function Columns(element, {
    doc,
    gridLines = true, 
    colClass = 'col', 
    breakpointMd = 480, 
    breakpointLg = 992
} = {}) {
    let windowWidth = doc.body.clientWidth;
    let oneColWidth = element.clientWidth / 12;

    function initColumn(el) {
        let resizer;
        let dragging = false
        let x = 0;

        if(isNaN(el.dataset.cols) || el.dataset.cols == 0) {
            el.dataset.cols = 12
        }

        function onMouseDown(event) {
            x = event.x;
            dragging = true
            element.classList.add('dragging')
            resizer.classList.add('dragging')
        }

        function onMouseMove(event) {
            if(dragging) {
                let field = 'cols'

                if(windowWidth > breakpointLg) {
                    field = 'colsLg'
                } else if(windowWidth > breakpointMd) {
                    field = 'colsMd'
                }
                
                const diffLength = event.x - x
                
                if(diffLength < -oneColWidth/2 || diffLength > oneColWidth / 2) {
                    console.log(el.dataset[field], field)
                    if(el.dataset[field] == 0) {
                        console.log(el.dataset['colsLg'], el.dataset['colsMd'], el.dataset['cols'])

                        if(field == 'colsLg' && el.dataset['colsMd']) {
                            el.dataset[field] = el.dataset['colsMd']
                        } 
                        el.dataset[field] = el.dataset['cols']
                        console.log(el.dataset[field])
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

            element.classList.remove('dragging')
            resizer.classList.remove('dragging')
        }

        function init() {
            resizer = doc.createElement('div')
            resizer.classList.add('resizer-handle')
            el.appendChild(resizer)

            resizer.addEventListener('mousedown', onMouseDown)
            doc.addEventListener('mousemove', onMouseMove)
            doc.addEventListener('mouseup', onMouseUp)
        }

        function destroy() {
            resizer.removeEventListener('mousedown', onMouseDown)
            doc.removeEventListener('mousemove', onMouseMove)
            doc.removeEventListener('mouseup', onMouseUp)

            resizer.remove()
        }

        init()
        return { init, destroy }
    }

    let columns = []

    function updateSize() {
        windowWidth = doc.body.clientWidth;
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
                const line = doc.createElement('div')
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