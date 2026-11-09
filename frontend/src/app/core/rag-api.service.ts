import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import {
  AskResponse,
  DocumentDetail,
  DocumentSummary,
  EvaluationResult,
  EvaluationRunSummary,
  HealthResponse,
  HistoryItem,
  SearchHit,
  TextUploadResponse,
} from './api.models';

@Injectable({ providedIn: 'root' })
export class RagApiService {
  private readonly http = inject(HttpClient);

  health(): Observable<HealthResponse> {
    return this.http.get<HealthResponse>('/health');
  }

  ingestText(name: string, text: string): Observable<TextUploadResponse> {
    return this.http.post<TextUploadResponse>('/api/policies/text', { name, text });
  }

  uploadFile(file: File): Observable<TextUploadResponse> {
    const form = new FormData();
    form.append('file', file, file.name);
    return this.http.post<TextUploadResponse>('/api/policies/upload', form);
  }

  listDocuments(): Observable<DocumentSummary[]> {
    return this.http.get<DocumentSummary[]>('/api/policies');
  }

  getDocument(id: string): Observable<DocumentDetail> {
    return this.http.get<DocumentDetail>(`/api/policies/${id}`);
  }

  deleteDocument(id: string): Observable<void> {
    return this.http.delete<void>(`/api/policies/${id}`);
  }

  search(question: string, topK: number): Observable<SearchHit[]> {
    return this.http.post<SearchHit[]>('/api/search', { question, topK });
  }

  ask(question: string, topK: number): Observable<AskResponse> {
    return this.http.post<AskResponse>('/api/ask', { question, topK });
  }

  history(): Observable<HistoryItem[]> {
    return this.http.get<HistoryItem[]>('/api/history');
  }

  feedback(questionId: string, helpful: boolean, comment?: string): Observable<void> {
    return this.http.post<void>(`/api/history/${questionId}/feedback`, { helpful, comment });
  }

  runEvaluation(topK: number): Observable<EvaluationResult & { runId: string }> {
    return this.http.post<EvaluationResult & { runId: string }>('/api/evaluate', { topK });
  }

  evaluationRuns(): Observable<EvaluationRunSummary[]> {
    return this.http.get<EvaluationRunSummary[]>('/api/evaluate/runs');
  }
}

export function errorText(error: unknown): string {
  const body = (error as { error?: unknown })?.error;
  if (typeof body === 'string' && body) {
    return body;
  }
  if (body && typeof body === 'object') {
    const record = body as Record<string, unknown>;
    const text = record['detail'] ?? record['title'] ?? record['message'];
    if (typeof text === 'string') {
      return text;
    }
  }
  const status = (error as { status?: number })?.status;
  if (status === 401) return 'Please sign in. / يرجى تسجيل الدخول.';
  if (status === 403) return 'Admin role required. / تحتاج صلاحية مدير.';
  return 'Request failed. Check that the API is running.';
}
