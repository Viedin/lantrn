import { defineConfig } from 'vitepress'

// https://vitepress.dev/reference/site-config
export default defineConfig({
  base: '/lantrn/',
  title: "Lantrn",
  description: "Self-hosted search for your documents",
  head: [['link', { rel: 'icon', href: '/lantrn/logo.svg' }]],
  ignoreDeadLinks: 'localhostLinks',
  themeConfig: {
    // https://vitepress.dev/reference/default-theme-config
    logo: '/logo.svg',
    nav: [
      { text: 'Guide', link: '/guide/getting-started' }
    ],

    sidebar: [
      {
        text: 'Using Lantrn',
        items: [
          { text: 'Getting started', link: '/guide/getting-started' },
          { text: 'Adding documents', link: '/guide/adding-documents' },
          { text: 'Searching', link: '/guide/searching' },
          { text: 'Users and access', link: '/guide/users' },
          { text: 'Public API', link: '/guide/api' },
          { text: 'Configuration', link: '/guide/configuration' }
        ]
      },
      {
        text: 'Contributing',
        items: [
          { text: 'Development', link: '/guide/development' }
        ]
      }
    ],

    socialLinks: [
      { icon: 'github', link: 'https://github.com/Viedin/lantrn' }
    ],

    search: { provider: 'local' }
  }
})
