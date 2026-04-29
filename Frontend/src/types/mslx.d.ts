declare module 'mslx-request' {
    const request: any;
    export default request;
}

declare global {
    interface Window {
        MSLX_Stores: any;
        MSLX_API: any;
        mslxRequest: any;
    }
}