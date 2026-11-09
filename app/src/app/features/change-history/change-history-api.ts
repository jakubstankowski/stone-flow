import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ChangeHistoryPage, ChangeHistoryQuery, OBJECT_TYPES } from './change-history-models';

/** Endpoint `GET /api/change-history` (Reporting, tylko odczyt). Błędy jako `HttpErrorResponse`. */
@Injectable({ providedIn: 'root' })
export class ChangeHistoryApi {
  private readonly http = inject(HttpClient);

  list(query: ChangeHistoryQuery): Promise<ChangeHistoryPage> {
    return firstValueFrom(
      this.http.get<ChangeHistoryPage>('/api/change-history', { params: listParams(query) }),
    );
  }
}

function listParams(query: ChangeHistoryQuery): HttpParams {
  let params = new HttpParams()
    .set('changedFrom', query.changedFrom)
    .set('changedTo', query.changedTo)
    .set('direction', query.direction)
    .set('page', query.page)
    .set('pageSize', query.pageSize);
  if (query.businessFrom !== '') {
    params = params.set('businessFrom', query.businessFrom);
  }
  if (query.businessTo !== '') {
    params = params.set('businessTo', query.businessTo);
  }
  for (const type of OBJECT_TYPES.filter((candidate) => query.objectTypes.includes(candidate))) {
    params = params.append('objectType', type);
  }
  return params;
}
