import '/_content/FluentCMS.Web.UI.Components/js/quill.min.js'
import { debounce } from '/_content/FluentCMS.Web.UI.Components/js/helpers.js'

const toolbarTypes = {
    basic: undefined,
    simple: [
        [{ header: [1, 2, 3, 4, 5, 6, false] }],
        ["bold", "italic", "underline", "strike"],
        ["blockquote", "code-block"],
    ],
    standard: [
        [{ header: [1, 2, 3, 4, 5, 6, false] }],
        ["bold", "italic", "underline", "strike"],
        ["blockquote", "code-block"],
        [{ list: "ordered" }, { list: "bullet" }],
        [{ direction: "rtl" }],
        [{ size: ["small", false, "large", "huge"] }],
        [{ color: [] }, { background: [] }],
        [{ font: [] }],
        [{ align: [] }],
        ["clean"],
    ],
    advanced: [
        [{ header: [1, 2, 3, 4, 5, 6, false] }],
        ["bold", "italic", "underline", "strike"],
        ["blockquote", "code-block"],
        [{ list: "ordered" }, { list: "bullet" }],
        [{ script: "sub" }, { script: "super" }],
        [{ indent: "-1" }, { indent: "+1" }],
        [{ direction: "rtl" }],
        [{ color: [] }, { background: [] }],
        [{ font: [] }],
        ["clean"],
    ],
};

const textEditors = new Map();

// https://github.com/slab/quill/issues/662#issuecomment-1623172474
function destroyQuill(instance, el) {
    if (!instance) return;

    instance.theme.modules.toolbar.container.remove();
    instance.theme.modules.clipboard.container.remove();
    instance.theme.tooltip.root.remove();

    el.classList.forEach((cls) => {
        if (cls.startsWith("ql-")) {
            requestAnimationFrame(() => {
                el.classList.remove(cls);
            });
        }
    });

    el.innerHTML = instance.root.innerHTML
}

export function update(dotnet, element, config) {
    const instance = textEditors.get(element)

    if ('value' in config) {
        instance.root.innerHTML = config.value
    }
}

export function initialize(dotnet, element, config) {
    dispose(dotnet, element);

    let instance = new Quill(element, {
        modules: {
            toolbar: toolbarTypes[config.type],
        },
        theme: "snow",
        placeholder: config.placeholder,
        readOnly: config.readonly,
    });
    
    if ('value' in config) {
        instance.root.innerHTML = config.value
    }

    const updateValue = debounce((delta, old, source) => {
        const innerHtml = instance.root.innerHTML;
        let value;
        if (innerHtml === "<p><br></p>")
            value = ""
        else
            value = innerHtml

        dotnet.invokeMethodAsync('UpdateValue', value)
    }, 50)

    instance.on('text-change', updateValue)

    textEditors.set(element, instance);
}

export function dispose(dotnet, element) {
    const textEditor = textEditors.get(element);

    destroyQuill(textEditor, element);

    textEditors.delete(element);
}
