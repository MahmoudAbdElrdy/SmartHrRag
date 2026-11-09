export interface HealthResponse {
  status: string;
}

export interface TextUploadResponse {
  documentId: string;
  duplicate: boolean;
  chunks: number;
}

export interface SearchHit {
  chunkId: string;
  documentId: string;
  documentName: string;
  section: string;
  text: string;
  score: number;
}

export interface AskResponse {
  answer: string;
  sources: SearchHit[];
  questionId?: string;
}

export interface DocumentSummary {
  id: string;
  name: string;
  sourceFile: string;
  status: string;
  error?: string | null;
  chunkCount: number;
  createdUtc: string;
}

export interface DocumentDetail {
  id: string;
  name: string;
  sourceFile: string;
  status: string;
  createdUtc: string;
  chunks: { id: string; sequence: number; section: string; content: string }[];
}

export interface HistoryItem {
  id: string;
  question: string;
  answer: string;
  sourcesJson: string;
  latencyMs: number;
  createdUtc: string;
  helpful: boolean | null;
}

export interface EvaluationCaseResult {
  question: string;
  expectedDocument: string | null;
  expectedSection: string | null;
  rank: number;
  retrieved: string[];
}

export interface EvaluationResult {
  questions: number;
  recallAtK: number;
  precisionAtK: number;
  mrr: number;
  cases: EvaluationCaseResult[];
}

export interface EvaluationRunSummary {
  id: string;
  createdUtc: string;
  topK: number;
  questions: number;
  recallAtK: number;
  precisionAtK: number;
  mrr: number;
}
