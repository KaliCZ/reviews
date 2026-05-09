// Wire DTOs come from `web/src/api`, hand-mirrored from the API's
// `backend/api/Models/Dtos.cs`. This file just re-exports the types the
// components consume so callers don't need to know the source path.

export type {
  ProductSummary,
  ProductDetail,
  ReviewItem,
  ReviewsPage,
  ReviewSort,
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
