import {
  ApplicationConfig,
  inject,
  provideAppInitializer,
  provideBrowserGlobalErrorListeners,
} from '@angular/core';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { MatIconRegistry } from '@angular/material/icon';
import { provideRouter, withInMemoryScrolling } from '@angular/router';

import { routes } from './app.routes';
import { credentialsInterceptor } from './core/http/credentials.interceptor';

/** Ligature icons come from the self-hosted `material-symbols` (outlined) font. */
export const MATERIAL_SYMBOLS_FONT_SET = 'material-symbols-outlined';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes, withInMemoryScrolling({ scrollPositionRestoration: 'top' })),
    provideHttpClient(withInterceptors([credentialsInterceptor])),
    provideAppInitializer(() => {
      inject(MatIconRegistry).setDefaultFontSetClass(MATERIAL_SYMBOLS_FONT_SET);
    }),
  ],
};
