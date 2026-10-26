import { Component, Input, Output, EventEmitter, ChangeDetectionStrategy } from '@angular/core';
import { DishListResponse } from '@app/models/dishes/dish-list-response.model';
import { tagLabel } from '@app/shared/helpers/dish-filter.helper';

@Component({
  selector: 'app-dish-modal',
  standalone: true,
  imports: [],
  templateUrl: './dish-modal.component.html',
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrl: './dish-modal.component.scss',
})
export class DishModalComponent {
  @Input() dish!: DishListResponse;
  @Input() preOrderEnabled = false;
  @Output() closed = new EventEmitter<void>();
  @Output() preOrderClicked = new EventEmitter<void>();

  get dietsText(): string {
    return this.tagsText(this.dish.diets, 'No special diet declared');
  }

  get allergensText(): string {
    return this.tagsText(this.dish.allergens, 'No allergens declared');
  }

  private tagsText(tags: string[] | undefined, emptyText: string): string {
    return tags && tags.length > 0 ? tags.map(tagLabel).join(', ') : emptyText;
  }

  onAdd(): void {
    this.preOrderClicked.emit();
  }

  onClose(): void {
    this.closed.emit();
  }
}
