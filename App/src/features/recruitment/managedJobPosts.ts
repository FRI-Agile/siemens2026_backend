import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { request } from '../../shared/api/http.ts'
import { useCommand } from '../../shared/api/useCommand.ts'
import { getAccessToken } from '../../shared/auth/staffAuth.ts'
import { useStaffAuth } from '../../shared/auth/StaffAuthContext.ts'
import type { EmploymentType, JobPostSort, PaymentType } from './vocabulary.ts'

/** Mirrors `JobPostStatuses` in RecruitmentVocabulary.cs. */
export const managedJobPostStatuses = ['draft', 'published', 'closed'] as const

export type ManagedJobPostStatus = (typeof managedJobPostStatuses)[number]

export interface ManagedJobPost {
  id: string
  title: string
  location: string
  department: string
  description: string
  status: ManagedJobPostStatus
  openPositions: number
  salary: number
  paymentType: PaymentType
  employmentType: EmploymentType
  recruiterId: string
  hiringManagerId: string
  createdAt: string
  createdBy: string
  updatedAt: string | null
  updatedBy: string | null
  version: number
}

export interface ManagedJobPostPage {
  items: ManagedJobPost[]
  /** How many posts match across all pages. */
  total: number
  offset: number
  limit: number
}

/** The body of `POST /api/v1/recruitment/job-posts/manage/search`; the API checks the same limits (1 to 100 a page). */
export interface ManagedJobPostFilter {
  offset: number
  limit: number
  /** Matched on the server against the title and location, ignoring case and accents; sorted by `relevance`, also forgiving a typo. */
  search?: string
  /** Any of these; every status when empty or left out. */
  statuses?: readonly ManagedJobPostStatus[]
  /** Any of these; every type when empty or left out. */
  employmentTypes?: readonly EmploymentType[]
  /** `relevance` (the best match, forgiving a typo) needs a `search`; the API sorts `newest` first when left out. */
  sort?: JobPostSort
}

export interface UpdateManagedJobPostBody {
  id: string
  title: string
  location: string
  department: string
  description: string
  status: Exclude<ManagedJobPostStatus, 'closed'>
  openPositions: number
  salary: number
  paymentType: PaymentType
  employmentType: EmploymentType
  recruiterId: string
  /** Left out, the post keeps its hiring manager; only an admin may change it. */
  hiringManagerId?: string
  version: number
}

type VersionCommand = { jobPostId: string; version: number }

/**
 * One page of every job post, filtered and searched on the server.
 *
 * Out-of-order answers cannot show stale results: the whole filter is the query key, so each search's answer is
 * cached under its own key and the page only ever reads the entry for the filter it shows now. An answer to an older
 * search that arrives late lands in its own entry and is never displayed in place of the newer one. The `signal` also
 * lets React Query abort the superseded request once nothing observes it, so it stops costing the server too.
 */
export function useManagedJobPosts(filter: ManagedJobPostFilter) {
  const auth = useStaffAuth()

  return useQuery({
    queryKey: ['recruitment', 'job-posts', 'managed', filter],
    queryFn: async ({ signal }) =>
      request<ManagedJobPostPage>(
        '/api/v1/recruitment/job-posts/manage/search',
        {
          method: 'POST',
          headers: {
            Authorization: `Bearer ${await getAccessToken(auth)}`,
            'Content-Type': 'application/json',
          },
          // An empty search or no type means no filter; the API reads a missing field the same way.
          body: JSON.stringify({
            offset: filter.offset,
            limit: filter.limit,
            search: filter.search || undefined,
            statuses: filter.statuses,
            employmentTypes: filter.employmentTypes,
            sort: filter.sort,
          }),
          signal,
        },
      ),
    // Keeps the current page on screen while the next one loads, instead of flashing an empty table.
    placeholderData: keepPreviousData,
  })
}

export function useManagedJobPost(id: string) {
  // TODO: a GET for one managed post; until then this finds it among the newest 100.
  const query = useManagedJobPosts({ offset: 0, limit: 100 })
  return {
    ...query,
    data: query.data?.items.find((post) => post.id === id),
  }
}

export function useUpdateManagedJobPost() {
  const auth = useStaffAuth()
  return useCommand(
    async ({ id, ...body }: UpdateManagedJobPostBody, idempotencyKey) =>
      request<ManagedJobPost>(
        `/api/v1/recruitment/job-posts/${encodeURIComponent(id)}`,
        {
          method: 'PUT',
          headers: {
            Authorization: `Bearer ${await getAccessToken(auth)}`,
            'Content-Type': 'application/json',
          },
          body: JSON.stringify(body),
          idempotencyKey,
        },
      ),
  )
}

export function useCloseManagedJobPost() {
  const auth = useStaffAuth()
  return useCommand(
    async ({ jobPostId, version }: VersionCommand, idempotencyKey) =>
      request<StatusChangeResponse>(
        `/api/v1/recruitment/job-posts/${encodeURIComponent(jobPostId)}/close`,
        {
          method: 'POST',
          headers: {
            Authorization: `Bearer ${await getAccessToken(auth)}`,
            'Content-Type': 'application/json',
          },
          body: JSON.stringify({ version }),
          idempotencyKey,
        },
      ),
  )
}

export function useReopenManagedJobPost() {
  const auth = useStaffAuth()
  return useCommand(
    async ({ jobPostId, version }: VersionCommand, idempotencyKey) =>
      request<StatusChangeResponse>(
        `/api/v1/recruitment/job-posts/${encodeURIComponent(jobPostId)}/reopen`,
        {
          method: 'POST',
          headers: {
            Authorization: `Bearer ${await getAccessToken(auth)}`,
            'Content-Type': 'application/json',
          },
          body: JSON.stringify({ version }),
          idempotencyKey,
        },
      ),
  )
}

export interface StatusChangeResponse {
  id: string
  status: ManagedJobPostStatus
  updatedAt: string | null
  version: number
}
