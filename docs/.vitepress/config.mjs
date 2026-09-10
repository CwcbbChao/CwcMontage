import { defineConfig } from 'vitepress'

export default defineConfig({
  title: 'CwcMontage',
  description: '面向 Unity 现代动作游戏的高性能纯表现层蒙太奇系统',
  base: '/CwcMontage/',
  lang: 'zh-CN',
  lastUpdated: true,
  cleanUrls: true,

  themeConfig: {
    siteTitle: 'CwcMontage',
    logo: '/images/logo.png',

    nav: [
      { text: '指南', link: '/guide/getting-started' },
      { text: '核心架构', link: '/architecture/interval-sweep' },
      { text: 'API 参考', link: '/api/overview' },
      { text: '路线图 & Pro', link: '/roadmap/' },
      { text: 'GitHub', link: 'https://github.com/CwcbbChao/CwcMontage' }
    ],

    sidebar: {
      '/guide/': [
        {
          text: '起步',
          items: [
            { text: '项目介绍', link: '/guide/introduction' },
            { text: '快速上手 (5分钟)', link: '/guide/getting-started' }
          ]
        },
        {
          text: '核心功能',
          items: [
            { text: '时间轴编辑器实操', link: '/guide/editor-guide' },
            { text: '去语义化物理分段', link: '/guide/physical-sections' },
            { text: '自定义 ActionBlock 扩展', link: '/guide/custom-block' },
            { text: 'Root Motion 委托对接', link: '/guide/root-motion' }
          ]
        }
      ],
      '/architecture/': [
        {
          text: '设计哲学与算法',
          items: [
            { text: '区间扫掠无漏帧算法', link: '/architecture/interval-sweep' },
            { text: '双缓冲槽 Playables 拓扑', link: '/architecture/ping-pong-mixer' }
          ]
        }
      ],
      '/api/': [
        {
          text: 'API 手册',
          items: [
            { text: '核心接口与句柄', link: '/api/overview' }
          ]
        }
      ],
      '/roadmap/': [
        {
          text: '未来演进',
          items: [
            { text: '路线图与 Pro 版展望', link: '/roadmap/' }
          ]
        }
      ]
    },

    search: {
      provider: 'local'
    },

    footer: {
      message: 'Released under Cwc Tools Public License (Free for End Products).',
      copyright: 'Copyright © 2026 Cwcbb'
    },

    socialLinks: [
      { icon: 'github', link: 'https://github.com/CwcbbChao/CwcMontage' }
    ]
  }
})
