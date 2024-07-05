/** @type {import('tailwindcss').Config} */
module.exports = {
  content: [
    "../**/*.{razor,html,cshtml}",
    "./node_modules/flowbite/**/*.js"
  ],
  theme: {
    extend: {
      colors: {
        primary: {
          50:        '#f4f8fd',
          100:       '#e8f1fb',
          200:       '#c6ddf4',
          300:       '#a3c8ed',
          400:       '#5e9fe0',
          500:       '#1976d2',
          600:       '#176abd',
          700:       '#13599e',
          800:       '#0f477e',
          900:       '#0c3a67',
          on:        '#f4f8fd',
          dark: {
            DEFAULT: '#a3c8ed',
            on:      '#0f477e', 
          }
        },
      },
    },
  },
  plugins: [
    require('flowbite/plugin'),
    require('flowbite-typography'),
  ],
}


