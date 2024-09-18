import '/_content/FluentCMS.Web.Plugins/js/quill.min.js'
import '/_content/FluentCMS.Web.Plugins/js/quill-image-resize.min.js'
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

    // var Link = toolbar.container.querySelector('.ql-link')

    // Link.innerHTML = `
    //     <svg xmlns="http://www.w3.org/2000/svg" width="32" height="32" viewBox="0 0 24 24"><path fill="currentColor" d="M10.59 13.41c.41.39.41 1.03 0 1.42c-.39.39-1.03.39-1.42 0a5.003 5.003 0 0 1 0-7.07l3.54-3.54a5.003 5.003 0 0 1 7.07 0a5.003 5.003 0 0 1 0 7.07l-1.49 1.49c.01-.82-.12-1.64-.4-2.42l.47-.48a2.98 2.98 0 0 0 0-4.24a2.98 2.98 0 0 0-4.24 0l-3.53 3.53a2.98 2.98 0 0 0 0 4.24m2.82-4.24c.39-.39 1.03-.39 1.42 0a5.003 5.003 0 0 1 0 7.07l-3.54 3.54a5.003 5.003 0 0 1-7.07 0a5.003 5.003 0 0 1 0-7.07l1.49-1.49c-.01.82.12 1.64.4 2.43l-.47.47a2.98 2.98 0 0 0 0 4.24a2.98 2.98 0 0 0 4.24 0l3.53-3.53a2.98 2.98 0 0 0 0-4.24a.973.973 0 0 1 0-1.42"/></svg>
    // `

    // TODO: Don't remove these Icons. will be used for image upload/external feature
    // toolbar.container.querySelector('.ql-imageExternal').innerHTML = `
    //     <svg xmlns="http://www.w3.org/2000/svg" width="32" height="32" viewBox="0 0 24 24"><path fill="none" stroke="currentColor" stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M10 5H8.2c-1.12 0-1.68 0-2.108.218a1.999 1.999 0 0 0-.874.874C5 6.52 5 7.08 5 8.2v7.6c0 1.12 0 1.68.218 2.108a2 2 0 0 0 .874.874c.427.218.987.218 2.105.218h7.606c1.118 0 1.677 0 2.104-.218c.377-.192.683-.498.875-.874c.218-.428.218-.987.218-2.105V14m1-5V4m0 0h-5m5 0l-7 7"/></svg>
    // `
    
    // toolbar.container.querySelector('.ql-imageUpload').innerHTML = `
    //     <svg xmlns="http://www.w3.org/2000/svg" width="32" height="32" viewBox="0 0 24 24"><path fill="currentColor" d="M19 13a1 1 0 0 0-1 1v.38l-1.48-1.48a2.79 2.79 0 0 0-3.93 0l-.7.7l-2.48-2.48a2.85 2.85 0 0 0-3.93 0L4 12.6V7a1 1 0 0 1 1-1h7a1 1 0 0 0 0-2H5a3 3 0 0 0-3 3v12a3 3 0 0 0 3 3h12a3 3 0 0 0 3-3v-5a1 1 0 0 0-1-1M5 20a1 1 0 0 1-1-1v-3.57l2.9-2.9a.79.79 0 0 1 1.09 0l3.17 3.17l4.3 4.3Zm13-1a.89.89 0 0 1-.18.53L13.31 15l.7-.7a.77.77 0 0 1 1.1 0L18 17.21Zm4.71-14.71l-3-3a1 1 0 0 0-.33-.21a1 1 0 0 0-.76 0a1 1 0 0 0-.33.21l-3 3a1 1 0 0 0 1.42 1.42L18 4.41V10a1 1 0 0 0 2 0V4.41l1.29 1.3a1 1 0 0 0 1.42 0a1 1 0 0 0 0-1.42"/></svg>
    // `

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
