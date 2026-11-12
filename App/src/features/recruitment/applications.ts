import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { request } from '../../shared/api/http.ts'
import { getAccessToken } from '../../shared/auth/staffAuth.ts'
import { useStaffAuth } from '../../shared/auth/StaffAuthContext.ts'

/** Where an application is in the pipeline; mirrors `ApplicationStatuses` in RecruitmentVocabulary.cs (Slovak codes). */
export const applicationStatuses = [
  'nová žiadosť',
  'predvýber',
  'pohovor',
  'ponuka',
  'prijatý',
  'odmietnutý',
] as const

export type ApplicationStatus = (typeof applicationStatuses)[number]

export const applicationStatusLabels: Record<ApplicationStatus, string> = {
  'nová žiadosť': 'Nová žiadosť',
  predvýber: 'Predvýber',
  pohovor: 'Pohovor',
  ponuka: 'Ponuka',
  prijatý: 'Prijatý',
  odmietnutý: 'Odmietnutý',
}

/** One applicant in `POST /job-posts/{id}/applications/search`. */
export interface ApplicationListItem {
  id: string
  firstName: string
  lastName: string
  email: string
  /** E.164 (`+421…`); `null` when the applicant gave none. */
  phoneNumber: string | null
  status: ApplicationStatus
  createdAt: string
}

export interface ApplicationPage {
  items: ApplicationListItem[]
  /** How many applicants match across all pages. */
  total: number
  offset: number
  limit: number
}

/** The body of `POST /job-posts/{id}/applications/search`; the API checks the same limits (1 to 100 a page). */
export interface ApplicationFilter {
  offset: number
  limit: number
  /** The applicant's name (ignoring case and accents, forgiving a typo) or e-mail (contains). */
  search?: string
  /** Any of these; every state when empty or left out. */
  statuses?: readonly ApplicationStatus[]
  sort?: 'newest' | 'oldest'
}

/**
 * One page of a job post's applicants, searched and filtered on the server. Only staff who may work with the post get
 * them; anyone else gets a 404. Each filter is its own query key, so a late answer to an older search never replaces
 * a newer one (see `useManagedJobPosts`).
 */
export function useJobPostApplications(
  jobPostId: string,
  filter: ApplicationFilter,
) {
  const auth = useStaffAuth()

  return useQuery({
    queryKey: ['recruitment', 'job-posts', jobPostId, 'applications', filter],
    queryFn: async ({ signal }) =>
      request<ApplicationPage>(
        `/api/v1/recruitment/job-posts/${encodeURIComponent(jobPostId)}/applications/search`,
        {
          method: 'POST',
          headers: {
            Authorization: `Bearer ${await getAccessToken(auth)}`,
            'Content-Type': 'application/json',
          },
          body: JSON.stringify(filter),
          signal,
        },
      ),
    placeholderData: keepPreviousData,
  })
}
