// Re-export from web/src/api so component imports stay stable.
export type {
  ProductSummary,
  ProductDetail,
  ReviewItem,
  ReviewsPage,
  ReviewSort,
  SortDirection,
  Rating,
  SubmitReviewRequest,
  EditReviewRequest,
  VoteRequest,
  AcceptedResponse,
  ConfigResponse,
  UploadedImage,
  AuthMe,
} from '../api';

export { Limits } from '../api';
