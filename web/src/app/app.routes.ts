import { Routes } from '@angular/router';

export const routes: Routes = [
  {
    path: '',
    loadComponent: () => import('./pages/product-list.page').then((m) => m.ProductListPage),
  },
  {
    path: 'products/:slug',
    loadComponent: () => import('./pages/product-detail.page').then((m) => m.ProductDetailPage),
  },
  {
    path: 'products/:slug/reviews',
    loadComponent: () => import('./pages/more-reviews.page').then((m) => m.MoreReviewsPage),
  },
  {
    path: 'products/:slug/review/new',
    loadComponent: () => import('./pages/submit-review.page').then((m) => m.SubmitReviewPage),
  },
  {
    path: 'products/:slug/review/:reviewId/edit',
    loadComponent: () => import('./pages/edit-review.page').then((m) => m.EditReviewPage),
  },
];
