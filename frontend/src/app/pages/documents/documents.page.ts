import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { DocumentDetail, DocumentSummary } from '../../core/api.models';
import { RagApiService, errorText } from '../../core/rag-api.service';

@Component({
  selector: 'app-documents',
  imports: [FormsModule, DatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <main class="page">
      <h1>Documents · المستندات</h1>
      @if (notice(); as n) {
        <div class="notice" [class.notice-error]="n.error" [class.notice-success]="!n.error">
          <span>{{ n.error ? '!' : '✓' }}</span><p>{{ n.text }}</p>
        </div>
      }

      <article class="card">
        <h2>Upload file · رفع ملف (.pdf .docx .md .txt)</h2>
        <form (ngSubmit)="upload()">
          <input type="file" accept=".pdf,.docx,.md,.txt" (change)="pick($event)" />
          <button class="button button-primary" type="submit" [disabled]="!file || busy()">
            {{ busy() ? 'Indexing… جاري الفهرسة' : 'Upload & index · رفع وفهرسة' }}
          </button>
        </form>
      </article>

      <article class="card">
        <h2>Paste text · لصق نص</h2>
        <form (ngSubmit)="ingestText()">
          <input name="name" [(ngModel)]="textName" placeholder="Policy name · اسم السياسة" />
          <textarea name="text" [(ngModel)]="text" rows="6" placeholder="## Section&#10;Policy text…"></textarea>
          <button class="button button-secondary" type="submit" [disabled]="busy()">Index text · فهرسة</button>
        </form>
      </article>

      <article class="card">
        <h2>Indexed documents ({{ docs().length }})</h2>
        <table class="data-table">
          <thead>
            <tr><th>Name</th><th>Source</th><th>Status</th><th>Chunks</th><th>Created</th><th></th></tr>
          </thead>
          <tbody>
            @for (d of docs(); track d.id) {
              <tr>
                <td>{{ d.name }}</td>
                <td>{{ d.sourceFile }}</td>
                <td>
                  <span class="badge" [class.badge-ok]="d.status === 'Ready'" [class.badge-bad]="d.status === 'Failed'">{{ d.status }}</span>
                </td>
                <td>{{ d.chunkCount }}</td>
                <td>{{ d.createdUtc | date: 'short' }}</td>
                <td class="row-actions">
                  <button class="button button-secondary" type="button" (click)="view(d.id)">View</button>
                  <button class="button button-secondary" type="button" (click)="remove(d)">Delete</button>
                </td>
              </tr>
            } @empty {
              <tr><td colspan="6">No documents yet · لا توجد مستندات</td></tr>
            }
          </tbody>
        </table>
      </article>

      @if (selected(); as doc) {
        <article class="card">
          <h2>{{ doc.name }} — {{ doc.chunks.length }} chunks</h2>
          @for (c of doc.chunks; track c.id) {
            <div class="chunk-view"><strong>#{{ c.sequence }} · {{ c.section }}</strong>
{{ c.content }}</div>
          }
        </article>
      }
    </main>
  `,
})
export class DocumentsPage {
  private readonly api = inject(RagApiService);
  readonly docs = signal<DocumentSummary[]>([]);
  readonly selected = signal<DocumentDetail | null>(null);
  readonly busy = signal(false);
  readonly notice = signal<{ text: string; error: boolean } | null>(null);
  file: File | null = null;
  textName = '';
  text = '';

  constructor() {
    this.refresh();
  }

  refresh(): void {
    this.api.listDocuments().subscribe({ next: (d) => this.docs.set(d), error: (e) => this.fail(e) });
  }

  pick(event: Event): void {
    this.file = (event.target as HTMLInputElement).files?.[0] ?? null;
  }

  upload(): void {
    if (!this.file) return;
    this.busy.set(true);
    this.api.uploadFile(this.file).subscribe({
      next: (r) => this.done(r.duplicate ? 'Already indexed (same content) · موجود مسبقاً' : `Indexed ${r.chunks} chunks · تمت الفهرسة`),
      error: (e) => this.fail(e),
    });
  }

  ingestText(): void {
    this.busy.set(true);
    this.api.ingestText(this.textName, this.text).subscribe({
      next: (r) => this.done(r.duplicate ? 'Already indexed · موجود مسبقاً' : `Indexed ${r.chunks} chunks · تمت الفهرسة`),
      error: (e) => this.fail(e),
    });
  }

  view(id: string): void {
    this.api.getDocument(id).subscribe({ next: (d) => this.selected.set(d), error: (e) => this.fail(e) });
  }

  remove(doc: DocumentSummary): void {
    if (!confirm(`Delete "${doc.name}"?`)) return;
    this.api.deleteDocument(doc.id).subscribe({
      next: () => {
        if (this.selected()?.id === doc.id) this.selected.set(null);
        this.done('Deleted · تم الحذف');
      },
      error: (e) => this.fail(e),
    });
  }

  private done(text: string): void {
    this.busy.set(false);
    this.notice.set({ text, error: false });
    this.refresh();
  }

  private fail(e: unknown): void {
    this.busy.set(false);
    this.notice.set({ text: errorText(e), error: true });
  }
}
