import '/_content/FluentCMS.Web.Plugins.Base/js/quill.min.js'
import '/_content/FluentCMS.Web.Plugins.Base/js/quill-image-resize.min.js'
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
        ["link", 'image'],
        ["clean"],
    ],
};

const textEditors = new Map();

// https://github.com/slab/quill/issues/662#issuecomment-1623172474
function destroyQuill(instance, el) {
    if (!instance) return;

    try {
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
    } catch(err) {
        // 
    }

}

export function update(dotnet, element, config) {
    const instance = textEditors.get(element)

    if ('value' in config) {
        instance.root.innerHTML = config.value
    }
}

export function setLink(dotnet, element, value) {
    const textEditor = textEditors.get(element)
    
    const href = value.href
    const text = value.text
    const mode = value.mode

    const range = textEditor.getSelection(true);

    const [leaf, offset] = textEditor.getLeaf(range.index)

    if(leaf.parent && leaf.parent.statics.blotName == 'link') {
        if(value.mode == 'Clear') {
            textEditor.setSelection(range.index - offset, leaf.parent.attributes.domNode.textContent.length)
            textEditor.format('link', false)   
            return;
        } else {
            leaf.parent.attributes.domNode.innerHTML = value.text
            leaf.parent.attributes.domNode.dataset.mode = value.mode
            leaf.parent.attributes.domNode.setAttribute('href', href)
        }
    } else {
        textEditor.deleteText(range.index, range.length)
        textEditor.insertText(range.index, text, 'link', {href, mode})
    }
    textEditor.setSelection(range.index + text.length)
}

export function openFileUpload(dotnet, element, value) {
    const id = value.id
    element = document.getElementById(id)

    if(element)
        element.click()
}

export function setImage(dotnet, element, value) {
    var textEditor = textEditors.get(element)

    const src = value.url
    const alt = value.alt
    const mode = value.mode

    const range = textEditor.getSelection(true);
    textEditor.insertText(range.index, '\n', Quill.sources.USER);
    textEditor.insertEmbed(range.index + 1, 'image', {
      alt,
      src,
      mode,
    }, Quill.sources.USER);
    textEditor.setSelection(range.index + 2, Quill.sources.SILENT);
}

class LinkModule extends Quill.imports["blots/inline"] {
    static blotName = 'link';
    static tagName = 'a'

    static create(value) {
        const node = super.create();
        node.setAttribute('href', value.href);
        node.setAttribute('data-mode', value.mode);
        node.setAttribute('_target', 'blank');

        return node;
    }

    static value(node) {
        return {
          href: node.getAttribute('href'),
          mode: node.dataset.mode,
          text: node.innerHTML
        };
    }
}

class ImageModule extends Quill.imports["blots/block/embed"] {
    static blotName = 'image';
    static tagName = 'img'

    static create(value) {
        const node = super.create();
        node.setAttribute('src', value.src);
        node.setAttribute('alt', value.alt);

        return node;
    }

    static value(node) {
        return {
          src: node.getAttribute('src'),
          alt: node.getAttribute('alt'),
        };
    }
}

export function initialize(dotnet, element, config) {
    dispose(dotnet, element);

    Quill.register('modules/resize', window.QuillResizeImage)
    Quill.register(LinkModule, true)
    Quill.register(ImageModule, true)

    const options = {
        modules: {
            toolbar: toolbarTypes[config.type],
            resize: {
                locale: {}
            }
        },
        theme: "snow",
        placeholder: config.placeholder,
        readOnly: config.readonly,
    }

    let instance = new Quill(element, options);
    
    const toolbar = instance.getModule('toolbar');

    function linkHandler() {
        const range = instance.getSelection(true)
        const text = instance.getText(range.index, range.length)
        
        const [leaf] = instance.getLeaf(range.index)

        let value = {
            text, 
            mode: text ? 'External' : 'Page',
            href: ''
        }

        if(leaf.parent && leaf.parent.statics.blotName == 'link') {
            const val = leaf.parent.statics.value(leaf.parent.attributes.domNode)
            
            value.href = val.href
            value.mode = val.mode
            value.text = val.text
        }

        dotnet.invokeMethodAsync('OpenLinkModal', value)
    }

    function imageHandler() {
        const value = {
            url: '',
            alt: '',
            mode: 'Library'
        }
        dotnet.invokeMethodAsync('OpenImageModal', value)
    }

    toolbar.addHandler('link', linkHandler)
    toolbar.addHandler('image', imageHandler)

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
