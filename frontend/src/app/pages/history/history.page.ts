import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { HistoryItem } from '../../core/api.models';
import { RagApiService, errorText } from '../../core/rag-api.service';

@Component({
  selector: 'app-history',
  imports: [DatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <main class="page">
      <h1>Question history · سجل الأسئلة</h1>
      @if (error(); as e) {
        <div class="notice notice-error"><span>!</span><p>{{ e }}</p></div>
      }
      <article class="card">
        <table class="data-table">
          <thead><tr><th>When</th><th>Question</th><th>Answer</th><th>ms</th><th>Feedback</th></tr></thead>
          <tbody>
            @for (h of items(); track h.id) {
              <tr>
                <td>{{ h.createdUtc | date: 'short' }}</td>
                <td>{{ h.question }}</td>
                <td style="white-space: pre-wrap">{{ h.answer }}</td>
                <td>{{ h.latencyMs }}</td>
                <td>{{ h.helpful === null ? '—' : h.helpful ? '👍' : '👎' }}</td>
              </tr>
            } @empty {
              <tr><td colspan="5">No questions yet</td></tr>
            }
          </tbody>
        </table>
      </article>
    </main>
  `,
})
export class HistoryPage {
  private readonly api = inject(RagApiService);
  readonly items = signal<HistoryItem[]>([]);
  readonly error = signal<string | null>(null);

  constructor() {
    this.api.history().subscribe({ next: (h) => this.items.set(h), error: (e) => this.error.set(errorText(e)) });
  }
}
