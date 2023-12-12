(function () {
    window.QuillFunctions = {
        createQuill: function (dotnetHelper, quillElement, quillOptions = {}) {
            var options = {
                debug: "info",
                modules: {
                    toolbar: [
                        [{ header: [1, 2, 3, 4, 5, 6, false] }],
                        [{ font: [] }],

                        ["bold", "italic", "underline", "strike"],
                        [{ align: [] }],

                        ["blockquote", "code-block"],
                        [{ list: "ordered" }, { list: "bullet" }],
                        [{ script: "sub" }, { script: "super" }],
                        [{ indent: "-1" }, { indent: "+1" }],
                        [{ direction: "rtl" }],
                        [{ color: [] }, { background: [] }],
                        ["clean"],
                    ],
                },
                placeholder: "Compose an epic...",
                readOnly: false,
                theme: "snow",
                ...quillOptions,
            };

            const instance = new Quill(quillElement, options);

            instance.on("text-change", function () {
                var htmlContent = instance.root.innerHTML;

                dotnetHelper.invokeMethodAsync("OnTextChanged", htmlContent);
            });
        },
        setQuillContent: function (quillControl, value) {
            if (value !== quillControl.__quill.root.innerHTML) {
                const delta = quillControl.__quill.clipboard.convert(value);
                quillControl.__quill.setContents(delta, "silent");
            }
        },
    };
})();
