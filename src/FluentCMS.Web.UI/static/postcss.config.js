module.exports = {

    plugins: [
        require('tailwindcss'),
        require('postcss-selector-replace')({before: ['.dark'], after: ['.f-theme-dark']})
    ]

}
