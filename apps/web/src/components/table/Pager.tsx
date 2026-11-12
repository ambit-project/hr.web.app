import { Pagination } from "@heroui/react";
import { useTranslation } from "react-i18next";

type PagerProps = { page: number; total: number; pageSize: number; onPage: (page: number) => void };

export function Pager({ page, total, pageSize, onPage }: PagerProps) {
	const { t } = useTranslation();
	const pages = Math.max(1, Math.ceil(total / pageSize));
	if (pages <= 1) return null;

	return (
		<Pagination className="justify-center">
			<Pagination.Content>
				<Pagination.Item>
					<Pagination.Previous isDisabled={page <= 1} onPress={() => onPage(page - 1)}>
						<Pagination.PreviousIcon />
						<span>{t("common.previous")}</span>
					</Pagination.Previous>
				</Pagination.Item>
				{Array.from({ length: pages }, (_, index) => index + 1).map((number) => (
					<Pagination.Item key={number}>
						<Pagination.Link isActive={number === page} onPress={() => onPage(number)}>
							{number}
						</Pagination.Link>
					</Pagination.Item>
				))}
				<Pagination.Item>
					<Pagination.Next isDisabled={page >= pages} onPress={() => onPage(page + 1)}>
						<span>{t("common.next")}</span>
						<Pagination.NextIcon />
					</Pagination.Next>
				</Pagination.Item>
			</Pagination.Content>
		</Pagination>
	);
}
