import { DatePipe, DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { EvaluationResult, EvaluationRunSummary } from '../../core/api.models';
import { RagApiService, errorText } from '../../core/rag-api.service';

@Component({
  selector: 'app-evaluation',
  imports: [FormsModule, DecimalPipe, DatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <main class="page">
      <h1>Retrieval evaluation · تقييم الاسترجاع</h1>
      @if (error(); as e) {
        <div class="notice notice-error"><span>!</span><p>{{ e }}</p></div>
      }
      <article class="card">
        <p class="card-description">Runs the seeded test questions (samples/evaluation.json) and checks if the expected document/section is in the top K.</p>
        <form (ngSubmit)="run()" class="row-actions">
          <label for="k">Top K</label>
          <select id="k" name="k" [(ngModel)]="topK">
            <option [ngValue]="1">1</option><option [ngValue]="3">3</option><option [ngValue]="5">5</option>
          </select>
          <button class="button button-primary" type="submit" [disabled]="busy()">
            {{ busy() ? 'Running… جاري التشغيل' : 'Run evaluation · تشغيل' }}
          </button>
        </form>
      </article>

      @if (result(); as r) {
        <article class="card">
          <div class="metrics">
            <div class="metric">Questions<strong>{{ r.questions }}</strong></div>
            <div class="metric">Recall&#64;K<strong>{{ r.recallAtK | number: '1.0-2' }}</strong></div>
            <div class="metric">Precision&#64;K<strong>{{ r.precisionAtK | number: '1.0-2' }}</strong></div>
            <div class="metric">MRR<strong>{{ r.mrr | number: '1.0-2' }}</strong></div>
          </div>
          <table class="data-table">
            <thead><tr><th>Question</th><th>Expected</th><th>Rank</th><th>Retrieved</th></tr></thead>
            <tbody>
              @for (c of r.cases; track $index) {
                <tr>
                  <td>{{ c.question }}</td>
                  <td>{{ c.expectedDocument }} › {{ c.expectedSection }}</td>
                  <td><span class="badge" [class.badge-ok]="c.rank > 0" [class.badge-bad]="c.rank === 0">{{ c.rank || 'miss' }}</span></td>
                  <td>@for (x of c.retrieved; track $index) { <div>{{ x }}</div> }</td>
                </tr>
              }
            </tbody>
          </table>
        </article>
      }

      <article class="card">
        <h2>Previous runs · التشغيلات السابقة</h2>
        <table class="data-table">
          <thead><tr><th>When</th><th>K</th><th>Questions</th><th>Recall</th><th>Precision</th><th>MRR</th></tr></thead>
          <tbody>
            @for (r of runs(); track r.id) {
              <tr>
                <td>{{ r.createdUtc | date: 'short' }}</td><td>{{ r.topK }}</td><td>{{ r.questions }}</td>
                <td>{{ r.recallAtK | number: '1.0-2' }}</td><td>{{ r.precisionAtK | number: '1.0-2' }}</td><td>{{ r.mrr | number: '1.0-2' }}</td>
              </tr>
            } @empty {
              <tr><td colspan="6">No runs yet</td></tr>
            }
          </tbody>
        </table>
      </article>
    </main>
  `,
})
export class EvaluationPage {
  private readonly api = inject(RagApiService);
  readonly result = signal<EvaluationResult | null>(null);
  readonly runs = signal<EvaluationRunSummary[]>([]);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  topK = 3;

  constructor() {
    this.loadRuns();
  }

  run(): void {
    this.busy.set(true);
    this.error.set(null);
    this.api.runEvaluation(this.topK).subscribe({
      next: (r) => {
        this.result.set(r);
        this.busy.set(false);
        this.loadRuns();
      },
      error: (e) => {
        this.error.set(errorText(e));
        this.busy.set(false);
      },
    });
  }

  private loadRuns(): void {
    this.api.evaluationRuns().subscribe({ next: (r) => this.runs.set(r), error: (e) => this.error.set(errorText(e)) });
  }
}
