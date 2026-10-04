import { Routes } from '@angular/router';
import { adminGuard } from './core/auth/guards';
import { NotFound } from './features/not-found/not-found';

export const routes: Routes = [
  {
    path: '',
    title: 'Find pickup hockey · Hockey Index',
    loadComponent: () => import('./features/discover/discover').then((m) => m.Discover),
  },
  {
    path: 'e/:id/:slug',
    title: 'Event · Hockey Index',
    loadComponent: () => import('./features/event-detail/event-detail').then((m) => m.EventDetail),
  },
  {
    path: 'e/:id',
    title: 'Event · Hockey Index',
    loadComponent: () => import('./features/event-detail/event-detail').then((m) => m.EventDetail),
  },
  {
    path: 'out',
    title: 'Leaving Hockey Index',
    loadComponent: () => import('./features/out/out').then((m) => m.Out),
  },
  {
    path: 'skill-levels',
    title: 'Skill levels · Hockey Index',
    loadComponent: () => import('./features/skill-levels/skill-levels').then((m) => m.SkillLevels),
  },
  {
    path: 'host',
    loadChildren: () => import('./features/host/host.routes').then((m) => m.hostRoutes),
  },
  {
    path: 'admin',
    canActivate: [adminGuard],
    loadChildren: () => import('./features/admin/admin.routes').then((m) => m.adminRoutes),
  },
  {
    path: 'auth/email/confirm',
    title: 'Confirm your email · Hockey Index',
    data: { purpose: 'confirm' },
    loadComponent: () => import('./features/host/email-confirm/email-link').then((m) => m.EmailLink),
  },
  {
    path: 'auth/email/login',
    title: 'Sign in with email · Hockey Index',
    data: { purpose: 'login' },
    loadComponent: () => import('./features/host/email-confirm/email-link').then((m) => m.EmailLink),
  },
  { path: '**', component: NotFound, title: 'Page not found' },
];
