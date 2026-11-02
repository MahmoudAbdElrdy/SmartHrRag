import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { AskResponse, HealthResponse, SearchHit, TextUploadResponse } from './api.models';

@Injectable({ providedIn: 'root' })
export class RagApiService {
  private readonly http = inject(HttpClient);

  health(): Observable<HealthResponse> {
    return this.http.get<HealthResponse>('/health');
  }

  ingestText(name: string, text: string): Observable<TextUploadResponse> {
    return this.http.post<TextUploadResponse>('/api/policies/text', { name, text });
  }

  search(question: string, topK: number): Observable<SearchHit[]> {
    return this.http.post<SearchHit[]>('/api/search', { question, topK });
  }

  ask(question: string, topK: number): Observable<AskResponse> {
    return this.http.post<AskResponse>('/api/ask', { question, topK });
  }
}
