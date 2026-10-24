import { type ApplicationConfig, inject, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideRouter, withHashLocation } from '@angular/router';
import { CTH_KNOB_PREFERENCES, CTH_METER_PREFERENCES } from '@cathode/audio-ui';
import { provideXuiECharts } from '@xui/echarts';
import { appRoutes } from './app.routes';
import { PreferencesStore, knobPreferences, meterPreferences } from './state/preferences.store';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    // Hash URLs: the desktop app serves the editor from a custom protocol with no server-side fallback route.
    provideRouter(appRoutes, withHashLocation()),
    // Loaded on first use: only the cab panel draws a chart.
    provideXuiECharts({ echarts: () => import('echarts') }),
    // The user's knob and meter preferences reach every knob and meter in the UI library.
    { provide: CTH_KNOB_PREFERENCES, useFactory: () => knobPreferences(inject(PreferencesStore)) },
    { provide: CTH_METER_PREFERENCES, useFactory: () => meterPreferences(inject(PreferencesStore)) }
  ]
};
