import { Routes } from '@angular/router';
import { AdminShell } from './admin-shell';

export const adminRoutes: Routes = [
  {
    path: '',
    component: AdminShell,
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'queue' },
      {
        path: 'queue',
        title: 'Moderation queue · Admin · Hockey Index',
        loadComponent: () => import('./queue/queue').then((m) => m.Queue),
      },
      {
        path: 'hosts',
        title: 'Hosts · Admin · Hockey Index',
        loadComponent: () => import('./hosts/host-lookup').then((m) => m.HostLookup),
      },
      {
        path: 'hosts/:id',
        title: 'Host · Admin · Hockey Index',
        loadComponent: () => import('./hosts/host-detail').then((m) => m.HostDetail),
      },
      {
        path: 'venues',
        title: 'Venues · Admin · Hockey Index',
        loadComponent: () => import('./venues/venues').then((m) => m.Venues),
      },
      {
        path: 'blocklist',
        title: 'Blocklist · Admin · Hockey Index',
        loadComponent: () => import('./blocklist/blocklist').then((m) => m.Blocklist),
      },
      {
        path: 'invites',
        title: 'Invites · Admin · Hockey Index',
        loadComponent: () => import('./invites/invites').then((m) => m.Invites),
      },
      {
        path: 'breakers',
        title: 'Circuit breakers · Admin · Hockey Index',
        loadComponent: () => import('./breakers/breakers').then((m) => m.Breakers),
      },
      {
        path: 'audit',
        title: 'Audit log · Admin · Hockey Index',
        loadComponent: () => import('./audit/audit').then((m) => m.Audit),
      },
    ],
  },
];
