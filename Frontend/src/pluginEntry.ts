import './style.css';
import STUNPage from "./views/STUNPage.vue";

export const pluginConfig = {
    name: 'STunPlugin',
    version: '1.0.0',

    // 注入路由
    routes: [
        // 插入在原有的一级菜单内
        {
            parentName: 'frp',
            path: 'stun',
            name: 'STUNPage',
            component: STUNPage,
            meta: { title: 'STUN 隧道', icon: 'internet', roleCode: ['admin'] },
        }
    ],
    // 注入组件
    extensions: []
};