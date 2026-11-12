import { Calendar, DateField, DatePicker, FieldError, Label } from "@heroui/react";
import { type CalendarDate, parseDate } from "@internationalized/date";
import { I18nProvider } from "react-aria-components";

// One fixed locale around date fields: otherwise the segment order rendered on the server can differ from the
// browser's and hydration fails. Values stay strings (YYYY-MM-DD); conversion happens only in this file.
const LOCALE = "pl-PL";

function toCalendarDate(value: string | null | undefined): CalendarDate | null {
	if (!value) return null;
	try {
		return parseDate(value);
	} catch {
		return null;
	}
}

type DayInputProps = {
	label: string;
	value: string | null | undefined;
	onChange: (value: string | null) => void;
	onBlur?: () => void;
	isInvalid?: boolean;
	errorMessage?: string | null;
	isRequired?: boolean;
	className?: string;
};

/** Day picker (HeroUI DatePicker); never the browser's native date input. */
export function DayInput({
	label,
	value,
	onChange,
	onBlur,
	isInvalid,
	errorMessage,
	isRequired,
	className,
}: DayInputProps) {
	return (
		<I18nProvider locale={LOCALE}>
			<DatePicker
				value={toCalendarDate(value)}
				onChange={(date) => onChange(date ? date.toString() : null)}
				onBlur={onBlur}
				isInvalid={isInvalid}
				isRequired={isRequired}
				validationBehavior="aria"
				className={className ?? "w-full"}
			>
				<Label>{label}</Label>
				<DateField.Group fullWidth>
					<DateField.Input>{(segment) => <DateField.Segment segment={segment} />}</DateField.Input>
					<DateField.Suffix>
						<DatePicker.Trigger aria-label={label}>
							<DatePicker.TriggerIndicator />
						</DatePicker.Trigger>
					</DateField.Suffix>
				</DateField.Group>
				<FieldError>{errorMessage}</FieldError>
				<DatePicker.Popover>
					<Calendar aria-label={label}>
						<Calendar.Header>
							<Calendar.YearPickerTrigger>
								<Calendar.YearPickerTriggerHeading />
								<Calendar.YearPickerTriggerIndicator />
							</Calendar.YearPickerTrigger>
							<Calendar.NavButton slot="previous" />
							<Calendar.NavButton slot="next" />
						</Calendar.Header>
						<Calendar.Grid>
							<Calendar.GridHeader>
								{(day) => <Calendar.HeaderCell>{day}</Calendar.HeaderCell>}
							</Calendar.GridHeader>
							<Calendar.GridBody>{(date) => <Calendar.Cell date={date} />}</Calendar.GridBody>
						</Calendar.Grid>
						<Calendar.YearPickerGrid>
							<Calendar.YearPickerGridBody>
								{({ year }) => <Calendar.YearPickerCell year={year} />}
							</Calendar.YearPickerGridBody>
						</Calendar.YearPickerGrid>
					</Calendar>
				</DatePicker.Popover>
			</DatePicker>
		</I18nProvider>
	);
}
