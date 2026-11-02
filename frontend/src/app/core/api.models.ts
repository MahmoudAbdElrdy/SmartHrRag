export interface HealthResponse {
  status: string;
}

export interface TextUploadResponse {
  documentId: string;
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
}
