import { Routes } from '@angular/router';
import { hostGuard } from '../../core/auth/guards';

export const hostRoutes: Routes = [
  {
    path: 'sign-in',
    title: 'Host sign in · Hockey Index',
    loadComponent: () => import('./sign-in/sign-in').then((m) => m.SignIn),
  },
  {
    path: '',
    canActivate: [hostGuard],
    children: [
      {
        path: '',
        title: 'Your events · Hockey Index',
        loadComponent: () => import('./dashboard/dashboard').then((m) => m.Dashboard),
      },
      {
        path: 'events/new',
        title: 'New event · Hockey Index',
        loadComponent: () => import('./event-editor/event-editor').then((m) => m.EventEditor),
      },
      {
        path: 'events/:id/edit',
        title: 'Edit event · Hockey Index',
        loadComponent: () => import('./event-editor/event-editor').then((m) => m.EventEditor),
      },
      {
        path: 'account',
        title: 'Account · Hockey Index',
        loadComponent: () => import('./account/account').then((m) => m.Account),
      },
    ],
  },
];
