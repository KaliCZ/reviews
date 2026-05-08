import { Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { StarRating } from '../components/star-rating';
import { ApiService } from '../services/api.service';
import { ProductSummary } from '../models';

@Component({
  imports: [RouterLink, StarRating],
  template: `
    <h1>Products</h1>
    <p class="muted">Browse the catalog. Click a product to read its reviews.</p>
    <ul class="grid">
      @for (p of products(); track p.id) {
        <li class="card">
          <a [routerLink]="['/products', p.slug]" class="card-link">
            @if (p.imageUrl) {
              <img [src]="p.imageUrl" [alt]="p.name" loading="lazy" />
            }
            <div class="card-body">
              <h2>{{ p.name }}</h2>
              <div class="meta">
                <app-star-rating [value]="p.averageRating" />
                <span class="count">{{ p.reviewCount }} reviews</span>
              </div>
            </div>
          </a>
        </li>
      } @empty {
        @if (loaded()) {
          <li class="empty">No products yet.</li>
        }
      }
    </ul>
  `,
  styles: [`
    h1 { margin: 0 0 0.25rem; }
    .muted { color: #666; margin-bottom: 1.5rem; }
    .grid { list-style: none; padding: 0; display: grid;
      grid-template-columns: repeat(auto-fill, minmax(220px, 1fr));
      gap: 1rem; }
    .card { border: 1px solid #e5e5e5; border-radius: 8px; overflow: hidden;
      background: #fff; transition: box-shadow .15s; }
    .card:hover { box-shadow: 0 4px 12px rgba(0,0,0,.08); }
    .card-link { color: inherit; text-decoration: none; display: block; }
    .card img { width: 100%; height: 140px; object-fit: cover; display: block; }
    .card-body { padding: 0.75rem; }
    .card h2 { font-size: 1rem; margin: 0 0 0.4rem; line-height: 1.25; }
    .meta { display: flex; align-items: center; gap: 0.5rem; }
    .count { color: #666; font-size: 0.85rem; }
    .empty { color: #888; }
  `],
})
export class ProductListPage {
  private readonly api = inject(ApiService);
  protected readonly products = signal<ProductSummary[]>([]);
  protected readonly loaded = signal(false);

  constructor() {
    this.api.listProducts().subscribe({
      next: (rows) => { this.products.set(rows); this.loaded.set(true); },
      error: () => this.loaded.set(true),
    });
  }
}
