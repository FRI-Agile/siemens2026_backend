import {
  FilterChecks,
  FilterChoice,
  JobPostListPage,
  JobPostListToolbar,
  JobPostPager,
  JobPostTable,
} from '../../../features/recruitment/jobPostList/JobPostList.tsx'
import {
  employmentTypeOptions,
  sortOptions,
  useJobPostListParams,
} from '../../../features/recruitment/jobPostList/useJobPostListParams.ts'
import { usePublishedJobPosts } from '../../../features/recruitment/publicJobPosts.ts'
import { problemMessage } from '../../../shared/api/problem.ts'

/** Posts per page. */
const PAGE_SIZE = 10

export function JobOffersPage() {
  const list = useJobPostListParams()
  const { data, isPending, isError, error, isPlaceholderData } =
    usePublishedJobPosts({
      offset: (list.page - 1) * PAGE_SIZE,
      limit: PAGE_SIZE,
      search: list.search || undefined,
      employmentTypes: list.employmentTypes,
      sort: list.sort,
    })

  const activeFilters =
    list.employmentTypes.length + Number(list.sort !== list.defaultSort)
  const message = isPending
    ? 'Načítavam pracovné pozície…'
    : isError
      ? problemMessage(error)
      : data.items.length > 0
        ? undefined
        : data.total > 0
          ? 'Na tejto strane už nie sú žiadne pracovné pozície.'
          : list.search || list.employmentTypes.length > 0
            ? 'Žiadna pracovná pozícia nezodpovedá zadanému filtru.'
            : 'Momentálne nemáme žiadne otvorené pracovné pozície.'

  return (
    <JobPostListPage
      hero
      heading="Aktuálne pracovné pozície"
      intro="Nájdite si príležitosť, ktorá vám vyhovuje. Kliknutím na pracovnú pozíciu zobrazíte všetky podrobnosti."
    >
      <JobPostListToolbar
        searchInput={list.searchInput}
        onSearchInput={list.setSearchInput}
        activeFilters={activeFilters}
        onClearFilters={() => list.clearFilters()}
      >
        <FilterChecks
          label="Typ pracovného pomeru"
          values={list.employmentTypes}
          options={employmentTypeOptions}
          onToggle={list.toggleEmploymentType}
        />
        <FilterChoice
          label="Zoradiť"
          value={list.sort}
          options={sortOptions(Boolean(list.search))}
          onChange={list.setSort}
        />
      </JobPostListToolbar>
      <JobPostTable
        items={data?.items ?? []}
        linkTo={(offer) => `/aktualne-pozicie/${offer.id}`}
        message={message}
        busy={isPlaceholderData}
      />
      {data && (
        <JobPostPager
          page={list.page}
          offset={data.offset}
          shown={data.items.length}
          total={data.total}
          disabled={isPlaceholderData}
          onPage={list.setPage}
        />
      )}
    </JobPostListPage>
  )
}
