import { type ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideRouter, withHashLocation } from '@angular/router';
import { provideXuiECharts } from '@xui/echarts';
import { appRoutes } from './app.routes';

export const appConfig: ApplicationConfig = {
    providers: [
        provideBrowserGlobalErrorListeners(),
        // Hash URLs: the desktop app serves the editor from a custom protocol with no server-side fallback route.
        provideRouter(appRoutes, withHashLocation()),
        // Loaded on first use: only the cab panel draws a chart.
        provideXuiECharts({ echarts: () => import('echarts') })
    ]
};
