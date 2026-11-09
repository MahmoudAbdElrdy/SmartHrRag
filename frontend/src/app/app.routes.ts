import { Routes } from '@angular/router';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'ask' },
  { path: 'ask', loadComponent: () => import('./pages/ask/ask.page').then((m) => m.AskPage) },
  {
    path: 'documents',
    loadComponent: () => import('./pages/documents/documents.page').then((m) => m.DocumentsPage),
  },
  {
    path: 'evaluation',
    loadComponent: () => import('./pages/evaluation/evaluation.page').then((m) => m.EvaluationPage),
  },
  {
    path: 'history',
    loadComponent: () => import('./pages/history/history.page').then((m) => m.HistoryPage),
  },
  { path: '**', redirectTo: 'ask' },
];
