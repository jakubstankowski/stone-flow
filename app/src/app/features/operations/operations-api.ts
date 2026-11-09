import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { OperationListPage, OperationListQuery } from './operation-models';

/** Endpoint `GET /api/operations` (Reporting). Błędy jako `HttpErrorResponse`. */
@Injectable({ providedIn: 'root' })
export class OperationsApi {
  private readonly http = inject(HttpClient);

  list(query: OperationListQuery): Promise<OperationListPage> {
    return firstValueFrom(
      this.http.get<OperationListPage>('/api/operations', { params: listParams(query) }),
    );
  }
}

function listParams(query: OperationListQuery): HttpParams {
  let params = new HttpParams()
    .set('from', query.from)
    .set('to', query.to)
    .set('direction', query.direction)
    .set('page', query.page)
    .set('pageSize', query.pageSize);
  for (const kind of query.kinds) {
    params = params.append('kind', kind);
  }
  return params;
}
