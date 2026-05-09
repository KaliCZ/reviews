// Wire DTOs come from `web/src/api`, which is generated from the API's
// OpenAPI spec by `npm run generate:client`. This file just re-exports the
// types the components consume so callers don't need to know the codegen
// path.

export type {
  ProductSummary,
  ProductDetail,
  ReviewItem,
  ReviewsPage,
  SubmitReviewRequest,
  EditReviewRequest,
  VoteRequest,
  AcceptedResponse,
  ConfigResponse,
  UploadedImage,
  AuthMe,
} from '../api';

export { Limits } from '../api';
