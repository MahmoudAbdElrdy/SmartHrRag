import { DatePipe, DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { EvaluationComparison, EvaluationRunSummary } from '../../core/api.models';
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
        <p class="card-description">
          Runs the seeded questions (samples/evaluation.json) in Vector, Keyword, Hybrid, and HybridRerank.
          Recall and MRR use answerable questions only. Unanswerable questions are timed and shown as n/a.
        </p>
        <form (ngSubmit)="run()" class="row-actions">
          <label for="k">Top K</label>
          <select id="k" name="k" [(ngModel)]="topK">
            <option [ngValue]="1">1</option>
            <option [ngValue]="3">3</option>
            <option [ngValue]="4">4</option>
            <option [ngValue]="5">5</option>
          </select>
          <button class="button button-primary" type="submit" [disabled]="busy()">
            {{ busy() ? 'Running… جاري التشغيل' : 'Compare modes · مقارنة الأوضاع' }}
          </button>
        </form>
      </article>

      @if (comparison(); as report) {
        @if (rerankNote(); as note) {
          <div class="notice"><span>i</span><p>{{ note }}</p></div>
        }
        <article class="card">
          <p class="card-description">Recall&#64;{{ report.topK }} and MRR. Latency is retrieval time per question, in milliseconds.</p>
          <table class="data-table">
            <thead>
              <tr>
                <th>Mode</th><th>Answerable</th><th>Recall&#64;K</th><th>MRR</th><th>Mean ms</th>
              </tr>
            </thead>
            <tbody>
              @for (mode of report.modes; track mode.mode) {
                <tr>
                  <td>{{ mode.mode }}</td>
                  <td>{{ mode.answerableQuestions }} / {{ mode.questions }}</td>
                  <td>{{ mode.recallAtK | number: '1.0-2' }}</td>
                  <td>{{ mode.mrr | number: '1.0-2' }}</td>
                  <td>{{ mode.meanLatencyMs | number: '1.0-0' }}</td>
                </tr>
              }
            </tbody>
          </table>
        </article>

        <article class="card">
          <div style="overflow-x: auto">
            <table class="data-table">
              <thead>
                <tr>
                  <th>Question</th>
                  @for (mode of report.modes; track mode.mode) {
                    <th>{{ mode.mode }}</th>
                  }
                </tr>
              </thead>
              <tbody>
                @for (row of rows(); track $index) {
                  <tr>
                    <td>
                      {{ row.question }}
                      @if (row.unanswerable) {
                        <div><span class="badge">unanswerable</span></div>
                      }
                    </td>
                    @for (cell of row.cells; track cell.mode) {
                      <td>
                        @if (row.unanswerable) {
                          <span class="badge">n/a</span>
                        } @else {
                          <span class="badge" [class.badge-ok]="cell.rank > 0" [class.badge-bad]="cell.rank === 0">
                            {{ cell.rank || 'miss' }}
                          </span>
                        }
                        <div>{{ cell.latencyMs }} ms</div>
                      </td>
                    }
                  </tr>
                }
              </tbody>
            </table>
          </div>
        </article>
      }

      <article class="card">
        <h2>Previous runs · التشغيلات السابقة</h2>
        <table class="data-table">
          <thead><tr><th>When</th><th>Mode</th><th>K</th><th>Questions</th><th>Recall</th><th>Precision</th><th>MRR</th></tr></thead>
          <tbody>
            @for (r of runs(); track r.id) {
              <tr>
                <td>{{ r.createdUtc | date: 'short' }}</td>
                <td>{{ r.mode || '—' }}</td>
                <td>{{ r.topK }}</td>
                <td>{{ r.questions }}</td>
                <td>{{ r.recallAtK | number: '1.0-2' }}</td>
                <td>{{ r.precisionAtK | number: '1.0-2' }}</td>
                <td>{{ r.mrr | number: '1.0-2' }}</td>
              </tr>
            } @empty {
              <tr><td colspan="7">No runs yet</td></tr>
            }
          </tbody>
        </table>
      </article>
    </main>
  `,
})
export class EvaluationPage {
  private readonly api = inject(RagApiService);
  readonly comparison = signal<EvaluationComparison | null>(null);
  readonly runs = signal<EvaluationRunSummary[]>([]);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  topK = 4;

  readonly rows = computed(() => {
    const modes = this.comparison()?.modes ?? [];
    const count = modes[0]?.cases.length ?? 0;
    return Array.from({ length: count }, (_, index) => ({
      question: modes[0].cases[index].question,
      unanswerable: modes[0].cases[index].unanswerable === true,
      cells: modes.map((mode) => ({
        mode: mode.mode ?? '',
        rank: mode.cases[index].rank,
        latencyMs: mode.cases[index].latencyMs ?? 0,
        retrieved: mode.cases[index].retrieved,
      })),
    }));
  });

  readonly rerankNote = computed(() => {
    const hybrid = this.comparison()?.modes.find((mode) => mode.mode === 'HybridRerank');
    if (!hybrid || hybrid.cases.some((item) => item.rerankerApplied)) {
      return null;
    }

    return hybrid.rerankerConfigured
      ? 'Reranker is configured but was not applied, so HybridRerank kept the fused order.'
      : 'Reranker is off, so HybridRerank matches hybrid fusion.';
  });

  constructor() {
    this.loadRuns();
  }

  run(): void {
    this.busy.set(true);
    this.error.set(null);
    this.api.compareEvaluation(this.topK).subscribe({
      next: (report) => {
        this.comparison.set(report);
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
    this.api.evaluationRuns().subscribe({
      next: (r) => this.runs.set(r),
      error: (e) => this.error.set(errorText(e)),
    });
  }
}
