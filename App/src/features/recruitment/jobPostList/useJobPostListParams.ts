import { useEffect, useState } from 'react'
import { useSearchParams } from 'react-router'
import {
  employmentTypeLabels,
  employmentTypes,
  jobPostSortLabels,
  jobPostSorts,
  type EmploymentType,
  type JobPostSort,
} from '../vocabulary.ts'

/** How long typing pauses before the search is sent, so a word typed is one request rather than one per key. */
const SEARCH_DELAY_MS = 300

/** The sorts as the URL spells them; `newest`, the default, is left out of it. */
const sortParams: Record<JobPostSort, string | undefined> = {
  newest: undefined,
  oldest: 'najstarsie',
  relevance: 'zhoda',
}

/** The filter menu's employment type choices; none ticked means every type. */
export const employmentTypeOptions = employmentTypes.map((type) => ({
  value: type,
  label: employmentTypeLabels[type],
}))

/** The filter menu's sorts; the best match only while there is a search text to match. */
export function sortOptions(hasSearch: boolean) {
  return jobPostSorts
    .filter((sort) => hasSearch || sort !== 'relevance')
    .map((sort) => ({ value: sort, label: jobPostSortLabels[sort] }))
}

/** Query keys and their new values: a list repeats the key once per value; `undefined` or `[]` removes it. */
export type ListParamChanges = Record<
  string,
  string | readonly string[] | undefined
>

/**
 * The values of a repeated query key that `allowed` knows, once each and in its order, so the same choice is always the
 * same URL and the same query key whatever order it was ticked in.
 */
export function listParam<T extends string>(
  params: URLSearchParams,
  key: string,
  allowed: readonly T[],
): T[] {
  const values = params.getAll(key)
  return allowed.filter((value) => values.includes(value))
}

/**
 * The URL's query with `changes` applied; a change to the filter, not the page, starts again on page 1. It starts from
 * the address bar rather than the router's `searchParams`: the router renders a navigation in a transition, so a
 * second change made before that render lands (two quick clicks in the filter menu) would otherwise undo the first.
 */
function withChanges(changes: ListParamChanges) {
  const next = new URLSearchParams(window.location.search)
  if (!('strana' in changes)) next.delete('strana')
  for (const [key, value] of Object.entries(changes)) {
    next.delete(key)
    for (const one of typeof value === 'string' ? [value] : (value ?? []))
      if (one) next.append(key, one)
  }
  return next
}

function sortFrom(value: string | null): JobPostSort {
  if (value === sortParams.oldest) return 'oldest'
  if (value === sortParams.relevance) return 'relevance'
  return 'newest'
}

/**
 * A job post list's page, search, employment types and sort, kept in the URL so the back button from a post returns
 * to the same list. Typing a search switches the sort to the best match, and clearing it returns to newest first; a
 * sort picked while searching is kept. Pages add their own filters through `params` and `update`.
 */
export function useJobPostListParams() {
  const [params, setParams] = useSearchParams()
  const page = Math.max(1, Number.parseInt(params.get('strana') ?? '', 10) || 1)
  const search = params.get('hladat') ?? ''
  const selectedTypes = listParam(params, 'typ', employmentTypes)
  const requestedSort = sortFrom(params.get('zoradenie'))
  // The best match needs a search text; a link that has none lists newest first.
  const sort: JobPostSort =
    requestedSort === 'relevance' && !search ? 'newest' : requestedSort
  /** The sort a search (or its absence) starts with, which the filter count does not count as a change. */
  const defaultSort: JobPostSort = search ? 'relevance' : 'newest'
  const [searchInput, setSearchInput] = useState(search)

  /** A filter change replaces the history entry; a page change adds one, so the back button steps through pages. */
  function update(changes: ListParamChanges) {
    setParams(() => withChanges(changes), {
      replace: !('strana' in changes),
    })
  }

  /**
   * Ticks or unticks `value` in the repeated query key `key`. Read from the address bar for the reason `withChanges`
   * gives, so two quick ticks both count.
   */
  function toggle<T extends string>(
    key: string,
    allowed: readonly T[],
    value: T,
  ) {
    const live = listParam(
      new URLSearchParams(window.location.search),
      key,
      allowed,
    )
    const ticked = live.includes(value)
    update({
      [key]: allowed.filter((v) => (v === value ? !ticked : live.includes(v))),
    })
  }

  // The search box is sent once typing pauses. Each sent term is its own query key, so a slow answer to an older term
  // can never replace the results of a newer one.
  useEffect(() => {
    const trimmed = searchInput.trim()
    if (trimmed === search) return
    const timer = setTimeout(
      () =>
        setParams(
          () => {
            const current = new URLSearchParams(window.location.search)
            const changes: ListParamChanges = { hladat: trimmed || undefined }
            if (trimmed && !current.get('hladat'))
              changes.zoradenie = sortParams.relevance
            else if (
              !trimmed &&
              current.get('zoradenie') === sortParams.relevance
            )
              changes.zoradenie = undefined
            return withChanges(changes)
          },
          { replace: true },
        ),
      SEARCH_DELAY_MS,
    )
    return () => clearTimeout(timer)
  }, [searchInput, search, setParams])

  return {
    params,
    page,
    search,
    searchInput,
    setSearchInput,
    employmentTypes: selectedTypes,
    sort,
    defaultSort,
    update,
    setPage: (next: number) =>
      update({ strana: next > 1 ? String(next) : undefined }),
    toggle,
    toggleEmploymentType: (type: EmploymentType) =>
      toggle('typ', employmentTypes, type),
    setSort: (next: JobPostSort) => update({ zoradenie: sortParams[next] }),
    /** Back to every type and the default sort, plus the page's own filters in `changes`; the search stays. */
    clearFilters: (changes: ListParamChanges = {}) =>
      update({
        typ: undefined,
        zoradenie: sortParams[defaultSort],
        ...changes,
      }),
  }
}
