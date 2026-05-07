import { Component, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';

interface HelloResponse {
  message: string;
  count: number;
}

@Component({
  selector: 'app-root',
  imports: [],
  templateUrl: './app.html',
  styleUrl: './app.css'
})
export class App {
  private readonly http = inject(HttpClient);

  protected readonly response = signal<HelloResponse | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly loading = signal(false);

  sayHello(): void {
    this.loading.set(true);
    this.error.set(null);
    this.http.get<HelloResponse>('/api/hello').subscribe({
      next: (res) => {
        this.response.set(res);
        this.loading.set(false);
      },
      error: (err) => {
        this.error.set(err.message ?? 'Request failed');
        this.loading.set(false);
      }
    });
  }
}
