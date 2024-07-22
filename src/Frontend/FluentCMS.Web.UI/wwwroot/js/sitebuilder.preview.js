import { Columns } from './columns.js';

window.sections = {}

window.initializeColumns = ({onResize} = {}) => {
    document.querySelectorAll('.f-row').forEach(row => {
        const column = new Columns(row, {
            gridLines: true,
            onResize,
            colClass: 'f-column',
            breakpointLg: 992,
            breakpointMd: 480,
        })
    
        column.init()
        sections[row.dataset.id] = column
    }) 
}
